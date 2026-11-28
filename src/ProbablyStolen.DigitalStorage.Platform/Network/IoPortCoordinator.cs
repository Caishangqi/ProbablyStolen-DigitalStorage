using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Energy;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Network;

/// <summary>
/// Runs every IO Port from its native one-second callback. A machine sleeps while it has no input cell or is paused
/// and then returns at once from the callback; slot, setting, module and network events wake it. An awake machine
/// moves a bounded batch each second, and waits a few seconds before retrying when nothing could move.
/// </summary>
internal sealed class IoPortCoordinator : IDisposable
{
    private const int SlotCount = IoPortSettings.SlotsPerBank;
    private const int RetrySeconds = 5;
    private static readonly IActionSource Actor = new MachineSource(IoPortItem.ItemId);
    private readonly NativeNetworkScanner _scanner;
    private readonly StorageService _storage;
    private readonly TransferDiagnostics _diagnostics;
    private readonly NativeNetworkEvents _events;
    private readonly Func<int> _frameCount;
    private readonly IIoPortTuning _tuning;
    // Every IO Port callback of one frame shares this budget, so many machines never add up to a visible hitch.
    private readonly long _frameBudgetTicks;
    private readonly IDisposable _networkEvents;
    private readonly IoPortTransferService _transfer = new();
    private readonly ConditionalWeakTable<GameItem, IoPortBanks> _banks = new();
    private readonly Dictionary<GameItem, Runtime> _runtime = new();
    private PlayerStore? _store;
    private StoreStation? _station;
    private EmporiumEntry? _entry;
    private (GameItem Machine, GameItem Cell, GameSlotInventory Slot)? _ejection;
    private int _frame = -1;
    private long _frameSpent;
    private bool _running;
    private Session? _stepping;
    private bool _disposed;

    internal IoPortCoordinator(NativeNetworkScanner scanner, StorageService storage, TransferDiagnostics diagnostics,
        NativeNetworkEvents events, IIoPortTuning tuning, Func<int> frameCount, TimeSpan? frameBudget = null)
    {
        _frameBudgetTicks = (long)((frameBudget ?? TimeSpan.FromMilliseconds(2)).TotalSeconds * Stopwatch.Frequency);
        (_scanner, _storage, _diagnostics, _events) = (scanner, storage, diagnostics, events);
        _frameCount = frameCount ?? throw new ArgumentNullException(nameof(frameCount));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _networkEvents = events.Subscribe(OnNetworkChanged);
        CellUpgradeInventory.ModulesChanged += OnCellSettingsChanged;
        CellPartitionTags.Committed += OnCellSettingsChanged;
        CellPages.UsageChanged += OnCellPagesChanged;
    }

    // Bind only from the initialized new-game or late-load lifecycle hook. Factory attachment may precede this hook.
    internal void StartRun(PlayerStore store, StoreStation station)
    {
        EndRun();
        if (_disposed || SaveManager.IsDecoding || EmporiumEntry.Instance == null || !PlayerStore.HasInstance ||
            !ReferenceEquals(PlayerStore.Instance, store)) return;
        (_store, _station, _entry) = (store, station, EmporiumEntry.Instance);
    }

    internal void EndRun()
    {
        _runtime.Clear();
        (_store, _station, _entry, _ejection) = (null, null, null, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _networkEvents.Dispose();
        CellUpgradeInventory.ModulesChanged -= OnCellSettingsChanged;
        CellPartitionTags.Committed -= OnCellSettingsChanged;
        CellPages.UsageChanged -= OnCellPagesChanged;
        EndRun();
    }

    internal void Attach(GameItem machine, IReadOnlyList<GameSlotInventory> left, IReadOnlyList<GameSlotInventory> right)
    {
        if (_disposed) return;
        if (machine.identifier != IoPortItem.ItemId || left.Count != SlotCount || right.Count != SlotCount ||
            left.Concat(right).Distinct().Count() != SlotCount * 2)
            throw new ArgumentException("An IO Port must have two distinct six-slot banks.");
        Detach(machine);
        _banks.Add(machine, new IoPortBanks(left.ToArray(), right.ToArray()));
    }

    internal void Detach(GameItem machine)
    {
        _banks.Remove(machine);
        _runtime.Remove(machine);
    }

    /// <summary>Wakes the machine after its cells or modules changed; called from native End callbacks.</summary>
    internal void Wake(GameItem machine)
    {
        if (_runtime.TryGetValue(machine, out var runtime)) runtime.Wake();
    }

    /// <summary>Gets whether the machine has a game second's work still spreading over frames.</summary>
    internal bool HasOpenSession(GameItem machine) => _runtime.TryGetValue(machine, out var runtime) && runtime.Session != null;

    internal bool IsEjecting(GameItem machine, GameItem cell, GameInventory slot) =>
        _ejection is { } move && ReferenceEquals(move.Machine, machine) && ReferenceEquals(move.Cell, cell) &&
        ReferenceEquals(move.Slot, slot) && move.Slot.childItem == null;

    internal IoPortDisplaySnapshot ReadDisplay(GameItem machine)
    {
        if (!IoPortNativeState.TryRead(machine, out var state)) return IoPortDisplaySnapshot.SettingsUnavailable;
        if (_runtime.TryGetValue(machine, out var runtime) && runtime.Display is { } display &&
            display.Settings?.SameAs(state.Settings) == true) return display;
        var status = !state.Settings.Running ? IoPortRuntimeStatus.Paused :
            HasInput(machine) ? IoPortRuntimeStatus.Working : IoPortRuntimeStatus.Idle;
        return new IoPortDisplaySnapshot(status, state.Settings, runtime?.Display?.Progress ?? IoPortProgress.Unknown);
    }

    /// <summary>Changes the settings; a direction change starts new jobs.</summary>
    internal bool TryChangeSettings(GameItem machine, Func<IoPortSettings, IoPortSettings> change)
    {
        if (_running || !IoPortNativeState.TryRead(machine, out var state)) return false;
        var settings = change(state.Settings);
        if (!IoPortNativeState.TryWrite(machine, state, state.WithSettings(settings))) return false;
        UpdateDisplay(machine, settings);
        return true;
    }

    /// <summary>Ends the current work: every fee counter restarts and the machine pauses. Moved items stay moved.</summary>
    internal bool Cancel(GameItem machine)
    {
        if (_running || !IoPortNativeState.TryRead(machine, out var state)) return false;
        var settings = state.Settings.WithRunning(false);
        if (!IoPortNativeState.TryWrite(machine, state, state.WithoutJobs().WithSettings(settings))) return false;
        UpdateDisplay(machine, settings, IoPortProgress.Unknown);
        return true;
    }

    internal void OnGameSecond(GameItem machine)
    {
        if (_running || !RunIsCurrent()) return;
        if (!TryGetMachine(machine, out var banks))
        {
            _runtime.Remove(machine);
            return;
        }
        if (!_runtime.TryGetValue(machine, out var runtime)) _runtime.Add(machine, runtime = new Runtime());
        // The shop clock calls each item at most once per frame. A second call in the same frame comes from the
        // compressed night simulation, which runs 360 game seconds behind a loading screen, so no frame budget applies.
        var frame = _frameCount();
        var compressed = runtime.LastFrame == frame;
        runtime.LastFrame = frame;
        if (runtime.Session is { } open)
        {
            // A second's quota does not carry over: a session still running gets a fresh one instead of a larger one.
            open.Credits = Math.Max(1, _tuning.RootsPerSecond);
            if (compressed) Step(machine, runtime, frame, compressed: true);
            return;
        }
        if (!runtime.IsDue()) return;

        if (!IoPortNativeState.TryRead(machine, out var state))
        {
            runtime.Sleep(IoPortDisplaySnapshot.SettingsUnavailable);
            return;
        }
        if (!state.Settings.Running)
        {
            // A machine that paused itself on finishing keeps saying so until the player changes something.
            runtime.Sleep(runtime.Display is { Status: IoPortRuntimeStatus.Completed } done && done.Settings?.SameAs(state.Settings) == true
                ? done : new IoPortDisplaySnapshot(IoPortRuntimeStatus.Paused, state.Settings, runtime.Progress));
            return;
        }
        if (banks.Left.All(slot => slot.childItem == null))
        {
            runtime.Sleep(new IoPortDisplaySnapshot(IoPortRuntimeStatus.Idle, state.Settings, IoPortProgress.Unknown));
            return;
        }

        runtime.Session = new Session(state.Settings, Math.Max(1, _tuning.RootsPerSecond), runtime.NextSlot);
        runtime.NextSlot = (runtime.NextSlot + 1) % SlotCount;
        Step(machine, runtime, frame, compressed);
    }

    /// <summary>
    /// Spends the open sessions' quotas a slice at a time. Called once per frame, so a second's work spreads over the
    /// frames that follow its game-second callback instead of landing in one frame.
    /// </summary>
    internal void OnFrame()
    {
        if (_running || !RunIsCurrent()) return;
        var frame = _frameCount();
        foreach (var pair in _runtime.Where(pair => pair.Value.Session != null).ToArray())
            Step(pair.Key, pair.Value, frame, compressed: false);
    }

    // Opens the session on its first step, moves roots for one slice, then either keeps the touched cells' contents for
    // the next frame or, once the quota is spent or nothing can move, closes the session.
    private void Step(GameItem machine, Runtime runtime, int frame, bool compressed)
    {
        if (runtime.Session is not { } session || !TryBeginSlice(frame, compressed, out var slice)) return;
        var started = Stopwatch.GetTimestamp();
        _running = true;
        _stepping = session;
        try
        {
            if (!TryGetMachine(machine, out var banks) || !IoPortNativeState.TryRead(machine, out var state) ||
                !state.Settings.SameAs(session.Settings) || session.Interrupted)
            {
                Drop(runtime, session, retrySeconds: 0);
                return;
            }
            if (session.Mounted == null)
            {
                if (!Open(machine, banks, runtime, session)) return;
            }
            else if (!session.ViewsAreCurrent())
            {
                // Another operation changed a cell this session holds; its views are stale. Start over next second.
                Drop(runtime, session, retrySeconds: 0);
                return;
            }

            var deadline = slice is { } length ? Stopwatch.GetTimestamp() + length : long.MaxValue;
            Move(machine, banks, session, ref state, deadline);
            if (session.IsFinished(banks)) Close(machine, banks, runtime, session, state);
            else Keep(banks, runtime, session, state);
        }
        catch (Exception)
        {
            _diagnostics.IoPortFault(-1, IoPortTransferFault.PreflightFailed);
            // Nothing is known about the session's cells after an unexpected failure; read them again next time.
            _storage.Checkpoint(Array.Empty<(GameItem, BasicCellInventory)>());
            foreach (var cell in session.Cells.Keys) CellInventoryCache.Forget(cell);
            runtime.Session = null;
            runtime.Retry(new IoPortDisplaySnapshot(IoPortRuntimeStatus.Faulted, session.Settings, IoPortProgress.Unknown), RetrySeconds);
        }
        finally
        {
            _running = false;
            _stepping = null;
            _frameSpent += Stopwatch.GetTimestamp() - started;
        }
    }

    private bool Open(GameItem machine, IoPortBanks banks, Runtime runtime, Session session)
    {
        var started = Stopwatch.GetTimestamp();
        var snapshot = _scanner.Capture(machine);
        var grid = snapshot.FindGrid(machine);
        if (_transfer.GetControllerBlock(grid) is { } offline)
        {
            End(runtime, new IoPortDisplaySnapshot(offline, session.Settings, runtime.Progress), RetrySeconds);
            return false;
        }
        var energy = new NativeTransferEnergyPort(snapshot.FindController(grid), grid.TotalPowerUsage, _store!, _station!);
        if (energy.GetFeeSourceBlock(0) is { } unpowered)
        {
            End(runtime, new IoPortDisplaySnapshot(unpowered, session.Settings, runtime.Progress), RetrySeconds);
            return false;
        }

        var cells = banks.Left.Select(slot => slot.childItem).Where(cell => cell != null).Cast<GameItem>().ToArray();
        var mounted = _storage.MountCaptured(snapshot, grid, cells, out var local);
        if (!mounted.Network.Online)
        {
            End(runtime, new IoPortDisplaySnapshot(IoPortRuntimeStatus.Unpowered, session.Settings, runtime.Progress), RetrySeconds);
            return false;
        }
        session.Opened(mounted, local, energy, Stopwatch.GetTimestamp() - started);
        return true;
    }

    private void Move(GameItem machine, IoPortBanks banks, Session session, ref IoPortNativeState state, long deadline)
    {
        var started = Stopwatch.GetTimestamp();
        var settings = session.Settings;
        var rootsPerFee = Math.Max(1, _tuning.RootsPerFee);
        bool HasTime() => Stopwatch.GetTimestamp() < deadline;
        var next = state;
        TransferProfile.Resume();
        try
        {
            for (var offset = 0; offset < SlotCount && session.Credits > 0 && HasTime(); offset++)
            {
                var slot = (session.FirstSlot + offset) % SlotCount;
                if (session.Finished[slot] || banks.Left[slot].childItem is not { } cell) continue;
                if (!TryFindLocal(session.Local, cell, out var inventory) || !TryCellId(cell, out var cellId))
                {
                    session.Status = Prefer(session.Status, IoPortRuntimeStatus.StorageUnavailable);
                    session.Finished[slot] = true;
                    continue;
                }

                var job = new IoPortTransferJob(next.CommittedRoots(slot, cellId), rootsPerFee);
                var cursor = session.Cursors[slot] ??= new IoPortTransferCursor();
                var cellStorage = new InventoryHandler(inventory, inventory.View.Partition,
                    allowInsertion: inventory.View.WriteState != CellWriteState.ExtractOnly);
                var batch = settings.Direction == IoPortDirection.Forward
                    ? _transfer.Transfer(job, cellStorage, session.Mounted!.Storage, Actor, session.Energy!, session.Energy!.GetFeeSourceBlock, session.Credits, HasTime, cursor)
                    : _transfer.Transfer(job, session.Mounted!.Storage, cellStorage, Actor, session.Energy!, session.Energy!.GetFeeSourceBlock, session.Credits, HasTime, cursor);
                session.Credits -= batch.MovedRoots;
                session.Moved += batch.MovedRoots;
                session.Last[slot] = batch;
                next = next.WithJob(slot, cellId, job.CommittedRootCount);
                session.Status = Prefer(session.Status, batch.Status);
                if (batch.Fault is { } fault)
                {
                    _diagnostics.IoPortFault(slot, fault);
                    _storage.MarkUncertain();
                }
                // A batch stopped only by the time slice or the quota continues; anything else is this second's answer.
                if (batch.Settled || batch.Status != IoPortRuntimeStatus.Working) session.Finished[slot] = true;
            }
        }
        finally
        {
            TransferProfile.Pause();
            session.Steps++;
            session.MoveTicks += Stopwatch.GetTimestamp() - started;
            WriteState(machine, ref state, next);
        }
    }

    // Keeps what this step changed, so the views stay the cells' contents into the next frame.
    private void Keep(IoPortBanks banks, Runtime runtime, Session session, IoPortNativeState state)
    {
        var started = Stopwatch.GetTimestamp();
        if (!_storage.Checkpoint(session.TakeTouched()))
        {
            Drop(runtime, session, retrySeconds: RetrySeconds);
            return;
        }
        session.RecordVersions();
        session.KeepTicks += Stopwatch.GetTimestamp() - started;
        runtime.Display = new IoPortDisplaySnapshot(session.Moved > 0 ? IoPortRuntimeStatus.Working : session.Status,
            session.Settings, session.Settings.Direction == IoPortDirection.Forward
                ? Tally(banks, session, state).Progress : runtime.Progress);
    }

    // Ends a session without using its views again. Cells it changed were kept or dropped at its last checkpoint.
    private void Drop(Runtime runtime, Session session, int retrySeconds)
    {
        End(runtime, new IoPortDisplaySnapshot(session.Moved > 0 ? IoPortRuntimeStatus.Working : session.Status,
            session.Settings, runtime.Progress), retrySeconds);
        if (session.Moved > 0) _events.Post(NetworkChangeReason.ContentsChanged);
    }

    private void Close(GameItem machine, IoPortBanks banks, Runtime runtime, Session session, IoPortNativeState state)
    {
        var settings = session.Settings;
        var (progress, done) = Tally(banks, session, state, NetworkPieces(session));

        var settled = Stopwatch.GetTimestamp();
        _storage.Settle(session.Mounted!, session.Local);
        var compacted = Stopwatch.GetTimestamp();
        // Removing emptied pages rebuilds a cell's page set, a cost that grows with the cell, and makes the next read
        // start over. It waits until the machine has nothing more to move, or a cell is about to leave, so it runs once
        // per job instead of every second. Uncompacted empty pages hold nothing and change no capacity.
        var sources = settings.Direction == IoPortDirection.Forward
            ? session.Local.Select(pair => pair.Cell) : session.Mounted!.Cells;
        runtime.PendingCompaction.UnionWith(sources.Where(session.WasTouched));
        var finishedWork = session.Moved == 0;
        foreach (var slot in done)
            if (banks.Left[slot].childItem is { } leaving) CompactPending(runtime, leaving);
        if (finishedWork) CompactPending(runtime);
        var closed = Stopwatch.GetTimestamp();
        _diagnostics.IoPortBatch(session.Moved, session.Quota, session.Steps, session.MountTicks, session.MoveTicks,
            session.KeepTicks + (compacted - settled), closed - compacted);

        var status = session.Credits <= 0 ? IoPortRuntimeStatus.Working : session.Status;
        var ejected = 0;
        foreach (var slot in done)
        {
            switch (Eject(machine, banks, slot))
            {
                case true:
                    ejected++;
                    WriteState(machine, ref state, state.WithoutJob(slot));
                    break;
                case false:
                    status = Prefer(status, IoPortRuntimeStatus.OutputFull);
                    break;
                default:
                    status = Prefer(status, IoPortRuntimeStatus.Faulted);
                    break;
            }
        }

        runtime.Session = null;
        if (session.Moved > 0) _events.Post(NetworkChangeReason.ContentsChanged);
        // Finished: every input cell has gone to the output bank. The machine pauses until the player starts it again.
        if (ejected > 0 && banks.Left.All(slot => slot.childItem == null) &&
            IoPortNativeState.TryWrite(machine, state, state.WithSettings(settings.WithRunning(false))))
        {
            runtime.Sleep(new IoPortDisplaySnapshot(IoPortRuntimeStatus.Completed, settings.WithRunning(false), progress));
            return;
        }
        var display = new IoPortDisplaySnapshot(status, settings, progress);
        runtime.Retry(display, session.Moved > 0 || ejected > 0 || status == IoPortRuntimeStatus.Working ? 0 : RetrySeconds);
    }

    // Compacts one pending cell, or all of them when no cell is named.
    private void CompactPending(Runtime runtime, GameItem? only = null)
    {
        if (only != null)
        {
            if (runtime.PendingCompaction.Remove(only)) _storage.Compact(only);
            return;
        }
        foreach (var cell in runtime.PendingCompaction.ToArray()) _storage.Compact(cell);
        runtime.PendingCompaction.Clear();
    }

    private static void End(Runtime runtime, IoPortDisplaySnapshot display, int retrySeconds)
    {
        runtime.Session = null;
        runtime.Retry(display, retrySeconds);
    }

    // What the network holds, by type, for the progress of filling cells from it; read once when a session closes.
    private static IReadOnlyDictionary<ItemKey, long>? NetworkPieces(Session session)
    {
        if (session.Settings.Direction != IoPortDirection.Reverse || session.Mounted == null) return null;
        var pieces = new KeyCounter();
        session.Mounted.Storage.GetAvailablePieces(pieces);
        return pieces.Snapshot();
    }

    // The session's progress over its input cells, and the slots whose cells meet the completion condition.
    private (IoPortProgress Progress, List<int> Done) Tally(IoPortBanks banks, Session session, IoPortNativeState state,
        IReadOnlyDictionary<ItemKey, long>? network = null)
    {
        var done = new List<int>();
        var progress = new ProgressTally(session.Settings.Direction);
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (banks.Left[slot].childItem is not { } cell || !TryFindLocal(session.Local, cell, out var inventory) ||
                !TryCellId(cell, out var cellId)) continue;
            progress.Add(state.CommittedRoots(slot, cellId), inventory, network);
            if (_transfer.IsCompletionSatisfied(session.Settings.CompletionMode, inventory, session.Last[slot])) done.Add(slot);
        }
        return (progress.Result, done);
    }

    // True when moved, false when no output slot is free, null when the move could not be verified.
    private bool? Eject(GameItem machine, IoPortBanks banks, int slot)
    {
        var input = banks.Left[slot];
        if (input.childItem is not { } cell || !cell.MayRemove() || cell.MaxNumRemove() != 1 || input.IsRemoveLocked())
            return false;
        foreach (var output in banks.Right)
        {
            if (FindPlacement(output) == null) continue;
            var move = ItemMove.Run(NativeItemMove.IntoCheckedSlot(cell, output,
                () => FindPlacement(output),
                () => input.childItem == null && FindPlacement(input, output: false) is { } back ? back : null));
            if (move.Outcome == ItemMoveOutcome.Moved && ReferenceEquals(output.childItem, cell)) return true;
            _diagnostics.IoPortFault(slot, move.Outcome == ItemMoveOutcome.Unchanged ? IoPortTransferFault.PreflightFailed :
                ReferenceEquals(input.childItem, cell) ? IoPortTransferFault.RootReturned : IoPortTransferFault.OwnershipUnverified);
            return move.Outcome == ItemMoveOutcome.Unchanged ? false : null;
        }
        return false;

        GridShape? FindPlacement(GameSlotInventory target, bool output = true)
        {
            if (target.childItem != null || target.childItems.Count != 0 || target.IsInsertLocked()) return null;
            SlotMarker? place;
            if (output) _ejection = (machine, cell, target);
            try { place = target.TryFindOneValidInventorySlot(cell); }
            finally { _ejection = null; }
            return place != null && ReferenceEquals(place.inventory, target) && place.targetItem == null &&
                place.numTransfer == 1 && GeneralHelper.MayOwnershipAcceptItem(cell, target) ? place.itemGridShape : null;
        }
    }

    // Gets how long this machine may spend moving roots; null means no limit (the night simulation).
    private bool TryBeginSlice(int frame, bool compressed, out long? slice)
    {
        slice = null;
        if (compressed) return true;
        if (frame != _frame)
        {
            _frame = frame;
            _frameSpent = 0;
        }
        var left = _frameBudgetTicks - _frameSpent;
        var awake = Math.Max(1, _runtime.Values.Count(runtime => runtime.IsAwake));
        slice = Math.Min(left, _frameBudgetTicks / awake);
        return left > 0;
    }

    private static void WriteState(GameItem machine, ref IoPortNativeState state, IoPortNativeState next)
    {
        if (ReferenceEquals(state, next)) return;
        if (IoPortNativeState.TryWrite(machine, state, next)) state = next;
    }

    private void UpdateDisplay(GameItem machine, IoPortSettings settings, IoPortProgress? progress = null)
    {
        if (!_runtime.TryGetValue(machine, out var runtime)) _runtime.Add(machine, runtime = new Runtime());
        var status = !settings.Running ? IoPortRuntimeStatus.Paused :
            HasInput(machine) ? IoPortRuntimeStatus.Working : IoPortRuntimeStatus.Idle;
        runtime.Display = new IoPortDisplaySnapshot(status, settings, progress ?? runtime.Progress);
        runtime.Wake();
    }

    private bool HasInput(GameItem machine) =>
        _banks.TryGetValue(machine, out var banks) && banks.Left.Any(slot => slot.childItem != null);

    private static bool TryFindLocal(IReadOnlyList<(GameItem Cell, BasicCellInventory Inventory)> local, GameItem cell,
        [NotNullWhen(true)] out BasicCellInventory? inventory)
    {
        inventory = local.FirstOrDefault(pair => ReferenceEquals(pair.Cell, cell)).Inventory;
        return inventory != null;
    }

    private static bool TryCellId(GameItem cell, out int id)
    {
        id = cell.unitCount == 1 ? cell.GetUniqueID() : 0;
        return id > 0;
    }

    private void OnCellSettingsChanged(GameItem cell)
    {
        foreach (var runtime in _runtime.Values) runtime.Wake();
    }

    private void OnNetworkChanged(NetworkChanged change)
    {
        // A contents change is caught by the cell versions; the network's shape or power changing is not.
        var reshaped = change.Reason is not (NetworkChangeReason.ContentsChanged or NetworkChangeReason.StoredItemChanged
            or NetworkChangeReason.PowerCycle);
        foreach (var runtime in _runtime.Values)
        {
            if (reshaped) runtime.Wake();
            else runtime.Rouse();
        }
    }

    // Our own moves report the cells they changed; a change by anything else makes a held view stale.
    private void OnCellPagesChanged(GameItem cell)
    {
        foreach (var runtime in _runtime.Values)
        {
            if (runtime.Session is not { } session || !session.Holds(cell)) continue;
            if (_running && ReferenceEquals(session, _stepping)) session.Touch(cell);
            else session.Interrupt();
        }
    }

    private bool TryGetMachine(GameItem machine, [NotNullWhen(true)] out IoPortBanks? banks)
    {
        banks = null;
        return machine.identifier == IoPortItem.ItemId && IsOnRunGrid(machine) && _banks.TryGetValue(machine, out banks) &&
            banks.All.All(slot => ReferenceEquals(slot.GetParentItem(), machine));
    }

    private bool RunIsCurrent()
    {
        if (_disposed || SaveManager.IsDecoding || _store == null || _station == null || _entry == null) return false;
        if (EmporiumEntry.Instance == _entry && PlayerStore.HasInstance && ReferenceEquals(PlayerStore.Instance, _store))
            return true;
        EndRun();
        return false;
    }

    private bool IsOnRunGrid(GameItem machine)
    {
        if (machine.unitCount <= 0 || machine is GameItemElement { } element && element.IsDestroyed() ||
            machine.parentInventory is not GameGridInventory grid || !grid.childItems.Contains(machine) ||
            _entry is not { } entry) return false;
        // These are the roots of EmporiumEntry.GetAllItems, used by both one-second dispatchers.
        return new GraphNodeStorage?[] { entry.invElement, entry.backInvinvElement, entry.frontInvinvElement,
            entry.docInvElement, entry.showcaseElement, entry.trashInvElement, entry.hiddenElement,
            entry.afterhourWindow, entry.vendingFountainElement, entry.vendingMachineElement }
            .Any(root => root != null && (ReferenceEquals(grid, root) || GraphUtils.FindParentEquals(grid, root)));
    }

    private static IoPortRuntimeStatus Prefer(IoPortRuntimeStatus current, IoPortRuntimeStatus next) =>
        Priority(next) > Priority(current) ? next : current;

    private static int Priority(IoPortRuntimeStatus status) => status switch
    {
        IoPortRuntimeStatus.Faulted => 7,
        IoPortRuntimeStatus.OutputFull => 6,
        IoPortRuntimeStatus.StorageUnavailable => 5,
        IoPortRuntimeStatus.Unpowered or IoPortRuntimeStatus.OperationFeeUnavailable or IoPortRuntimeStatus.UnsupportedEnergySource => 4,
        IoPortRuntimeStatus.Working => 3,
        IoPortRuntimeStatus.WaitingForCapacity => 2,
        IoPortRuntimeStatus.NoEligibleContent => 1,
        _ => 0
    };

    // Per-machine scheduling: asleep until an event, or due after a number of callbacks.
    private sealed class Runtime
    {
        private bool _asleep;
        private int _delay;

        internal IoPortDisplaySnapshot? Display { get; set; }
        internal IoPortProgress Progress => Display?.Progress ?? IoPortProgress.Unknown;
        internal int NextSlot { get; set; }
        internal Session? Session { get; set; }
        internal HashSet<GameItem> PendingCompaction { get; } = new();
        internal int LastFrame { get; set; } = -1;
        internal bool IsAwake => !_asleep;

        internal bool IsDue()
        {
            if (_asleep) return false;
            if (_delay <= 0) return true;
            _delay--;
            return false;
        }

        // Its cells, modules or settings changed: whatever the open session read no longer holds.
        internal void Wake()
        {
            (_asleep, _delay) = (false, 0);
            Session?.Interrupt();
        }

        // New contents may let a waiting machine move again, without making the open session's views stale.
        internal void Rouse() => (_asleep, _delay) = (false, 0);

        internal void Sleep(IoPortDisplaySnapshot display) => (Display, _asleep) = (display, true);

        internal void Retry(IoPortDisplaySnapshot display, int seconds) => (Display, _asleep, _delay) = (display, false, seconds);
    }

    // One machine's work for one game second, spread over as many frames as its quota needs. The mounted views stay
    // valid across frames only while no other operation changes the cells: every step checks the cells' versions,
    // and every step that moved roots keeps the touched cells' contents before the frame ends.
    private sealed class Session
    {
        private readonly Dictionary<GameItem, long> _versions = new();
        private readonly HashSet<GameItem> _touched = new();
        private readonly HashSet<GameItem> _everTouched = new();

        internal Session(IoPortSettings settings, int quota, int firstSlot) =>
            (Settings, Quota, Credits, FirstSlot) = (settings, quota, quota, firstSlot);

        internal IoPortSettings Settings { get; }
        internal int Quota { get; }
        internal int Credits { get; set; }
        internal int FirstSlot { get; }
        internal MountedNetwork? Mounted { get; private set; }
        internal IReadOnlyList<(GameItem Cell, BasicCellInventory Inventory)> Local { get; private set; } =
            Array.Empty<(GameItem, BasicCellInventory)>();
        internal NativeTransferEnergyPort? Energy { get; private set; }
        internal Dictionary<GameItem, BasicCellInventory> Cells { get; } = new();
        internal bool[] Finished { get; } = new bool[SlotCount];
        internal IoPortBatchResult?[] Last { get; } = new IoPortBatchResult?[SlotCount];
        internal IoPortRuntimeStatus Status { get; set; } = IoPortRuntimeStatus.NoEligibleContent;
        internal int Moved { get; set; }
        internal int Steps { get; set; }
        internal long MountTicks { get; private set; }
        internal long MoveTicks { get; set; }
        internal long KeepTicks { get; set; }
        internal bool Interrupted { get; private set; }
        internal bool HasTouched => _touched.Count > 0;
        internal IoPortTransferCursor?[] Cursors { get; } = new IoPortTransferCursor?[SlotCount];

        internal bool WasTouched(GameItem cell) => _everTouched.Contains(cell);

        internal void Opened(MountedNetwork mounted, IReadOnlyList<(GameItem Cell, BasicCellInventory Inventory)> local,
            NativeTransferEnergyPort energy, long mountTicks)
        {
            (Mounted, Local, Energy, MountTicks) = (mounted, local, energy, mountTicks);
            foreach (var (cell, inventory) in mounted.Inventories) Cells[cell] = inventory;
            foreach (var (cell, inventory) in local) Cells[cell] = inventory;
            TransferProfile.Reset();
            RecordVersions();
        }

        internal void Interrupt() => Interrupted = true;

        internal bool Holds(GameItem cell) => Cells.ContainsKey(cell);

        internal void Touch(GameItem cell)
        {
            if (!Cells.ContainsKey(cell)) return;
            _touched.Add(cell);
            _everTouched.Add(cell);
        }

        internal IEnumerable<(GameItem Cell, BasicCellInventory Inventory)> TakeTouched()
        {
            var touched = _touched.Select(cell => (cell, Cells[cell])).ToArray();
            _touched.Clear();
            return touched;
        }

        internal void RecordVersions()
        {
            foreach (var cell in Cells.Keys) _versions[cell] = CellInventoryCache.Version(cell);
        }

        internal bool ViewsAreCurrent() => _versions.All(pair => CellInventoryCache.Version(pair.Key) == pair.Value);

        internal bool IsFinished(IoPortBanks banks) => Credits <= 0 ||
            Enumerable.Range(0, SlotCount).All(slot => Finished[slot] || banks.Left[slot].childItem == null);
    }

    // Forward: roots moved against roots still in the input cells. Reverse: roots moved against the network roots the
    // input cells' partitions still accept. Each cell's moved count is its job's verified total.
    private sealed class ProgressTally
    {
        private readonly IoPortDirection _direction;
        private long _moved;
        private long _remaining;

        internal ProgressTally(IoPortDirection direction) => _direction = direction;

        internal void Add(long moved, BasicCellInventory cell, IReadOnlyDictionary<ItemKey, long>? network)
        {
            _moved += moved;
            if (_direction == IoPortDirection.Forward)
            {
                var pieces = new KeyCounter();
                cell.GetAvailablePieces(pieces);
                _remaining += pieces.Snapshot().Values.Sum();
            }
            else if (network != null)
            {
                var partition = cell.View.Partition;
                foreach (var pair in network)
                    if (partition.Rejection(pair.Key) == null) _remaining += pair.Value;
            }
        }

        internal IoPortProgress Result => IoPortProgress.FromRoots(_moved, _remaining);
    }
}

internal sealed class IoPortBanks
{
    internal IoPortBanks(GameSlotInventory[] left, GameSlotInventory[] right)
    {
        Left = left;
        Right = right;
        All = left.Concat(right).ToArray();
    }

    internal GameSlotInventory[] Left { get; }
    internal GameSlotInventory[] Right { get; }
    internal GameSlotInventory[] All { get; }
}
