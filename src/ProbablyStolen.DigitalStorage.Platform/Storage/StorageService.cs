using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.ExtractDestination;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Queries;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Identity;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Terminal store, extract and read operations over the cells of one live network. Every call scans the network
/// again; items only move through <see cref="CellPages"/>, so the pages stay the only record of what is stored. Each
/// cell's contents are kept between calls by <see cref="CellInventoryCache"/>: an operation that ends with every move
/// accounted for keeps them, any other ending drops them so the next read starts over.
/// </summary>
internal sealed class StorageService
{
    private const string UnavailableKey = "terminal_storage_unavailable";
    private const string RestoreUnavailableKey = "storage_restore_unavailable";
    private readonly NativeNetworkScanner _scanner;
    private readonly Func<NativeGridSnapshot, Grid, bool> _isPowered;
    private readonly CellDiagnostics _diagnostics;
    private readonly Action<string> _nativeLog;
    private const string NoSpaceKey = "terminal_storage_reject_no_space";
    private static readonly IActionSource Player = new PlayerSource("player");
    private readonly Func<GameItem, bool> _migrate;
    private bool _viewsStale;
    // Set when a move ended without being accounted for: a failed native move or an exception during an operation.
    private bool _contentsUncertain;
    private readonly ConditionalWeakTable<GameItem, Dictionary<ItemKey, GameItem>> _terminalSamples = new();
    // Name, picture and search text per type. They follow from the stored state, which the key encodes, and from the
    // language; only the amounts change between reads.
    private Dictionary<ItemKey, TerminalEntry> _entryTexts = new();
    private bool? _restoreApplied;

    internal StorageService(NativeNetworkScanner scanner, Func<NativeGridSnapshot, Grid, bool> isPowered,
        Action<string> nativeLog, Func<GameItem, bool> migrate, CellDiagnostics diagnostics)
    {
        _nativeLog = nativeLog;
        _migrate = migrate ?? throw new ArgumentNullException(nameof(migrate));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _isPowered = isPowered ?? throw new ArgumentNullException(nameof(isPowered));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    private const string UniqueComponent = "digital_storage.unique";
    private static readonly ConditionalWeakTable<GameItem, ItemKey> FallbackKeys = new();
    private static readonly Dictionary<ItemKey, FallbackReason> FallbackReasons = new();
    private readonly HashSet<(string, FallbackReason)> _reportedFallbacks = new();
    private readonly HashSet<ItemKey> _anomalyChecked = new();
    private readonly HashSet<(string, string, string)> _reportedAnomalies = new();
    private static long _nextFallback;

    /// <summary>
    /// Gets an item's type. Items whose full state cannot be read get a fallback key that is unique to that item
    /// for this session: they can still be stored, show as their own terminal entry, and never match a partition.
    /// Mod items get null and are never stored.
    /// </summary>
    internal static ItemKey? Identify(GameItem item) => Identify(item, out _);

    /// <summary>Gets an item's type and, for a fallback key, why its state could not be read.</summary>
    internal static ItemKey? Identify(GameItem item, out NativeItemIdentityFailure failure)
    {
        failure = NativeItemIdentityFailure.None;
        if (item == null || StoreAdmission.IsModItem(item))
        {
            return null;
        }

        var mark = TransferProfile.Mark();
        var result = NativeItemIdentityReader.Read(item);
        TransferProfile.Add(TransferPart.Identify, mark);
        if (result.Status == NativeItemIdentityReadStatus.Success)
        {
            return result.Identity;
        }

        failure = result.Failure;
        var reason = new FallbackReason(result.Failure, result.Rule, result.At);
        return FallbackKeys.GetValue(item, unread =>
        {
            var key = new ItemKey(string.IsNullOrWhiteSpace(unread.identifier) ? "unknown" : unread.identifier,
                new[] { new KeyValuePair<string, string>(UniqueComponent,
                    Interlocked.Increment(ref _nextFallback).ToString(CultureInfo.InvariantCulture)) });
            FallbackReasons[key] = reason;
            return key;
        });
    }

    /// <summary>Gets why a fallback key's item could not be read, or false for a key that was read normally.</summary>
    internal static bool TryGetFallbackReason(ItemKey key, out FallbackReason reason) =>
        FallbackReasons.TryGetValue(key, out reason);

    /// <summary>
    /// Reads the aggregated contents of every available cell on the terminal's network: amounts from each cell's
    /// usage per type, and one stored root per type as the entry's sample.
    /// </summary>
    internal TerminalInventorySnapshot GetCachedInventory(GameItem terminal)
    {
        var samples = _terminalSamples.GetOrCreateValue(terminal);
        samples.Clear();
        var mounted = GetInventory(terminal, migratePending: false);
        if (!mounted.Network.Online)
        {
            return new TerminalInventorySnapshot(false, Array.Empty<TerminalEntry>(), mounted.Network.StorageUnavailable);
        }

        // A cell that could not be mounted, such as while a save is decoding, is unavailable, never empty.
        var unavailable = mounted.Network.StorageUnavailable || mounted.Inventories.Count < mounted.Network.Cells.Count ||
            mounted.Inventories.Any(entry => entry.Item2.View.Pages.Any(page => page.IsProtected));

        var totals = new Dictionary<ItemKey, (long Pieces, long Units)>();
        foreach (var (_, inventory) in mounted.Inventories)
        {
            foreach (var type in inventory.View.Usage.Types)
            {
                totals[type.Key] = totals.TryGetValue(type.Key, out var total)
                    ? (total.Pieces + type.Value.Pieces, total.Units + type.Value.Units)
                    : (type.Value.Pieces, type.Value.Units);
            }
        }

        foreach (var (cell, inventory) in mounted.Inventories)
        {
            foreach (var (key, root) in RootsByType(cell, inventory.View))
            {
                if (!samples.ContainsKey(key)) samples[key] = root;
            }
        }

        var texts = new Dictionary<ItemKey, TerminalEntry>();
        var entries = new List<TerminalEntry>(totals.Count);
        foreach (var pair in totals)
        {
            if (!samples.TryGetValue(pair.Key, out var sample)) continue;
            // A fallback key is unique to its item, so such items never share an entry; the reason is logged once.
            if (_diagnostics.IsEnabled(LogVerbosity.Info) && FallbackReasons.TryGetValue(pair.Key, out var reason) && _reportedFallbacks.Add((sample.identifier, reason)))
                _diagnostics.Write(LogVerbosity.Info, "identity_fallback", "Stored item identity uses a fallback.",
                    () => reason.Rule == null
                        ? new[] { LogField.Of("item_id", sample.identifier), LogField.Of("reason", reason.Failure.ToString()) }
                        : new[]
                        {
                            LogField.Of("item_id", sample.identifier), LogField.Of("reason", reason.Failure.ToString()),
                            LogField.Of("rule", reason.Rule), LogField.Of("at", reason.At ?? "")
                        });
            ReportAnomalies(pair.Key, sample.identifier);
            if (!_entryTexts.TryGetValue(pair.Key, out var text)) text = Entry(pair.Key, 0, 0, sample);
            texts[pair.Key] = text;
            entries.Add(new TerminalEntry(pair.Key, pair.Value.Pieces, text.DisplayName, text.SpriteKey, text.SpriteWidth,
                text.SpriteHeight, document: text.Document, totalUnits: pair.Value.Units));
        }

        // Only types still stored keep their texts and stay marked as checked for anomalies.
        _entryTexts = texts;
        _anomalyChecked.IntersectWith(totals.Keys);
        return new TerminalInventorySnapshot(true, entries, unavailable);
    }

    // Values other mods wrote that the game would not, such as NaN or a lone surrogate, are kept in the type as they
    // are; each is reported once per item and place. A stored type is checked once while it stays stored.
    private void ReportAnomalies(ItemKey key, string itemId)
    {
        if (!key.IsCanonical || !_diagnostics.IsEnabled(LogVerbosity.Warning) || !_anomalyChecked.Add(key)) return;
        foreach (var (rule, at) in NativeItemIdentityReader.FindAnomalies(key))
        {
            if (!_reportedAnomalies.Add((itemId, rule, at))) continue;
            _diagnostics.Write(LogVerbosity.Warning, "identity_anomaly",
                "Stored item holds a value the game would not write; it is kept as it is.",
                () => new[] { LogField.Of("item_id", itemId), LogField.Of("rule", rule), LogField.Of("at", at) });
        }
    }

    /// <summary>Drops the kept entry texts, so the next read builds them again in the current language.</summary>
    internal void ForgetEntryTexts() => _entryTexts = new Dictionary<ItemKey, TerminalEntry>();

    /// <summary>
    /// Finds an actual root of a type in the terminal's storage, used for native tooltips and item pictures. The root
    /// remains in its locked page; this method never creates a display clone. The last read's root is reused while it
    /// still sits on a storage page; otherwise the cells are searched by their recorded slot types.
    /// </summary>
    internal GameItem? FindSample(GameItem terminal, ItemKey key)
    {
        if (FindPreviewSample(terminal, key) is { } cached) return cached;

        var mounted = GetInventory(terminal, migratePending: false);
        foreach (var (cell, inventory) in mounted.Inventories)
        {
            if (!inventory.View.Contains(key)) continue;
            foreach (var (type, root) in RootsByType(cell, inventory.View))
            {
                if (key.Equals(type)) return root;
            }
        }

        return null;
    }

    // Hover uses the terminal's last snapshot and treats a missing sample as unknown without mounting the network.
    internal GameItem? FindPreviewSample(GameItem terminal, ItemKey key)
    {
        if (!_terminalSamples.TryGetValue(terminal, out var samples) || !samples.TryGetValue(key, out var root) ||
            !IsStored(root) || root.parentInventory is not GameGridInventory page) return null;
        var cell = page.GetParentItem();
        return cell != null && CellHealth.Get(cell).Status != CellHealthStatus.Protected ? root : null;
    }

    // Each stored root with its type: a strip root's type is its slot record; anchor and legacy roots are read.
    private static IEnumerable<(ItemKey Key, GameItem Root)> RootsByType(GameItem cell, CellView view)
    {
        var pages = CellPages.Pages(cell);
        for (var index = 0; index < pages.Count && index < view.Pages.Count; index++)
        {
            var page = view.Pages[index];
            if (page.IsProtected) continue;
            foreach (var root in pages[index].childItems)
            {
                var key = (page.Kind == PageKind.Strip
                    ? view.KeyAt(index, StripLayout.SlotIndexAt(page.Geometry, root.modifiedShape.minX, root.modifiedShape.minY))
                    : null) ?? Identify(root);
                if (key != null) yield return (key, root);
            }
        }
    }

    // Only storage page grids have insertion locked by this mod (CellPages.AddPage).
    private static bool IsStoredIn(GameItem item, IReadOnlyList<GameItem> cells) =>
        !(item is GameItemElement element && element.IsDestroyed()) && item.parentInventory is GameGridInventory page &&
        cells.Any(cell => CellPages.Pages(cell).Contains(page));

    private static bool IsStored(GameItem item) =>
        !(item is GameItemElement element && element.IsDestroyed()) &&
        item.parentInventory is GameGridInventory page && page.overrideLockInsert;

    /// <summary>
    /// Mounts every usable cell of the terminal's network. Pending schema 1
    /// cells, such as cells assembled during this session, are migrated first. Locked and unavailable cells are not
    /// mounted; cells restricted to extraction are mounted without insertion.
    /// </summary>
    internal MountedNetwork GetInventory(GameItem terminal) => GetInventory(terminal, migratePending: true);

    /// <summary>
    /// Mounts the network of any network machine for the public API. Unlike terminal reads it never migrates pending
    /// cells; those are migrated when a game loads or a terminal opens.
    /// </summary>
    internal MountedNetwork Mount(GameItem machine) => GetInventory(machine, migratePending: false);

    // Simulations pass false because checking availability must not trigger a migration that moves items.
    private MountedNetwork GetInventory(GameItem terminal, bool migratePending, TransferOperation? operation = null) =>
        Mount(Connect(terminal), migratePending, operation);

    private MountedNetwork Mount(Network network, bool migratePending, TransferOperation? operation)
    {
        var storage = new NetworkStorage();
        var inventories = new List<(GameItem, BasicCellInventory)>();
        if (!network.Online) return new MountedNetwork(network, storage, inventories);

        Action storedIdentityChanged = () => _viewsStale = true;
        foreach (var cell in network.Cells)
        {
            if (migratePending) _migrate(cell);
            var inventory = BasicCellHandler.GetCellInventory(cell, _diagnostics, storedIdentityChanged, operation);
            if (inventory == null) continue;
            var readOnly = inventory.View.WriteState == CellWriteState.ExtractOnly;
            storage.Mount(inventory.View.Priority,
                new InventoryHandler(inventory, inventory.View.Partition, allowInsertion: !readOnly));
            inventories.Add((cell, inventory));
        }

        return new MountedNetwork(network, storage, inventories);
    }

    /// <summary>
    /// Ends an operation on mounted cells. When every move was accounted for, the cells' contents are kept as they are
    /// now; otherwise they are dropped. Call before <see cref="CellPages.Compact"/>, whose page changes then simply
    /// make the next read start over for that cell.
    /// </summary>
    internal void Settle(MountedNetwork mounted)
    {
        foreach (var (cell, inventory) in mounted.Inventories)
        {
            if (_contentsUncertain || _viewsStale) CellInventoryCache.Forget(cell);
            else CellInventoryCache.Remember(cell, inventory.View);
        }

        _contentsUncertain = false;
        _viewsStale = false;
    }

    internal BasicCellInventory? ReadCell(GameItem cell) => BasicCellHandler.GetCellInventory(cell, _diagnostics);

    /// <summary>
    /// Mounts the drives of a grid the IO Port already captured and reads its input cells the same way, so one
    /// <see cref="Settle(MountedNetwork, IReadOnlyList{ValueTuple{GameItem, BasicCellInventory}})"/> keeps or drops
    /// every touched cell's contents. Never migrates pending cells.
    /// </summary>
    internal MountedNetwork MountCaptured(NativeGridSnapshot snapshot, Grid grid, IReadOnlyList<GameItem> localCells,
        out IReadOnlyList<(GameItem Cell, BasicCellInventory Inventory)> local)
    {
        _contentsUncertain = false;
        _viewsStale = false;
        var mounted = Mount(Connect(snapshot, grid), migratePending: false, operation: null);
        Action storedIdentityChanged = () => _viewsStale = true;
        var read = new List<(GameItem, BasicCellInventory)>(localCells.Count);
        foreach (var cell in localCells)
            if (BasicCellHandler.GetCellInventory(cell, _diagnostics, storedIdentityChanged) is { } inventory)
                read.Add((cell, inventory));
        local = read;
        return mounted;
    }

    /// <summary>
    /// Keeps the contents of the cells an IO Port step changed, so its views stay valid into the next frame. Returns
    /// false, after dropping those cells' contents, when a move of the step was not accounted for; the caller then
    /// stops using its views. Either way the next step starts with no recorded uncertainty.
    /// </summary>
    internal bool Checkpoint(IEnumerable<(GameItem Cell, BasicCellInventory Inventory)> touched)
    {
        var certain = !_contentsUncertain && !_viewsStale;
        foreach (var (cell, inventory) in touched)
        {
            if (certain) CellInventoryCache.Remember(cell, inventory.View);
            else CellInventoryCache.Forget(cell);
        }
        _contentsUncertain = false;
        _viewsStale = false;
        return certain;
    }

    /// <summary>Settles the mounted drives and the IO Port's own input cells together.</summary>
    internal void Settle(MountedNetwork mounted, IReadOnlyList<(GameItem Cell, BasicCellInventory Inventory)> local)
    {
        foreach (var (cell, inventory) in local)
        {
            if (_contentsUncertain || _viewsStale) CellInventoryCache.Forget(cell);
            else CellInventoryCache.Remember(cell, inventory.View);
        }
        Settle(mounted);
    }

    internal void Compact(GameItem cell) => CellPages.Compact(cell, _diagnostics);

    /// <summary>Records that a move ended without being accounted for, so the operation's cells are read again.</summary>
    internal void MarkUncertain() => _contentsUncertain = true;

    /// <summary>Stores each item independently in selection order; stored items are never rolled back.</summary>
    internal TerminalInsertionResult Store(GameItem terminal, IReadOnlyList<GameItem> items, TransferOperation? operation)
    {
        if (!IsRestoreApplied())
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.Offline, 0, RestoreUnavailableKey);
        }

        var mounted = GetInventory(terminal, migratePending: true, operation);
        if (!mounted.Network.Online)
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.Offline, 0, UnavailableKey);
        }

        _contentsUncertain = false;
        _viewsStale = false;
        long stored = 0;
        var candidates = 0;
        string? firstRejection = null;
        foreach (var item in items.Where(item => item != null).Distinct())
        {
            candidates++;
            var units = item.unitCount;
            if (_viewsStale)
            {
                Settle(mounted);
                mounted = GetInventory(terminal, migratePending: false, operation);
            }

            var rejection = TryStore(mounted, item, Actionable.Modulate, operation);
            if (rejection == null)
            {
                stored += units;
            }
            else
            {
                firstRejection ??= rejection;
                if (operation == null)
                    _nativeLog($"Digital Storage: store rejected item={item.identifier} units={item.unitCount} " +
                        $"size={CellPages.SizeOf(item)} reason={rejection}");
            }
        }

        Settle(mounted);

        if (candidates == 0)
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.NoItems, 0, "terminal_storage_capacity_rejected");
        }

        if (stored == 0)
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.Rejected, 0, firstRejection ?? NoSpaceKey);
        }

        // Partly stored batches report the stored amount and the first reason the rest stayed behind.
        return TerminalInsertionResult.Create(TerminalInsertionStatus.Success, stored,
            firstRejection == null ? "terminal_storage_inserted" : "terminal_storage_inserted_partial");
    }

    /// <summary>
    /// Gets, for each item, why it could not be stored right now as a text key, or null; nothing moves. Each item is
    /// checked alone against the current contents. The network is mounted once for the whole list, and not at all
    /// when every item is refused before storage is asked.
    /// </summary>
    internal string?[] StoreRefusals(GameItem terminal, IReadOnlyList<GameItem> items)
    {
        var refusals = new string?[items.Count];
        var pending = false;
        for (var index = 0; index < items.Count; index++)
        {
            refusals[index] = IsRestoreApplied() ? StoreAdmission.Refusal(items[index]) : RestoreUnavailableKey;
            pending |= refusals[index] == null;
        }

        if (!pending) return refusals;
        var mounted = GetInventory(terminal, migratePending: false);
        for (var index = 0; index < items.Count; index++)
        {
            if (refusals[index] != null) continue;
            refusals[index] = mounted.Network.Online ? TryStore(mounted, items[index], Actionable.Simulate) : UnavailableKey;
        }

        return refusals;
    }

    /// <summary>
    /// Extracts up to <paramref name="pieces"/> whole items of one type. The chosen <paramref name="priority"/> sets
    /// which inventory is tried first for each item. An item that neither inventory can fit stays in storage.
    /// </summary>
    internal TerminalExtractionResult ExtractPieces(GameItem terminal, ItemKey key, long pieces, ExtractionPriority priority,
        TransferOperation operation)
    {
        if (pieces <= 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.InvalidAmount, pieces, 0,
                "terminal_storage_invalid_amount");
        }

        var mounted = GetInventory(terminal);
        if (!mounted.Network.Online)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Offline, pieces, 0, UnavailableKey);
        }

        var available = new KeyCounter();
        mounted.Storage.GetAvailablePieces(available);
        if (available.Get(key) == 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.NoItems, pieces, 0, "terminal_storage_no_items");
        }

        // Extraction starts at the lowest priority, so drives with higher priority keep their items longest.
        var destination = new PieceLimitSink(ClickDestinations(priority, operation), pieces);
        _contentsUncertain = false;
        var failed = false;
        try
        {
            var result = mounted.Storage.Extract(key, long.MaxValue, destination, Actionable.Modulate, Player);
            if (result.StoppedBy.Outcome == TransferOutcome.Unowned) failed = _contentsUncertain = true;
        }
        catch (Exception error)
        {
            failed = _contentsUncertain = true;
            operation.Record(error);
        }
        finally
        {
            Settle(mounted);
            foreach (var cell in mounted.Cells) CellPages.Compact(cell, _diagnostics);
        }

        var moved = destination.Accepted;
        if (failed)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Failed, pieces, moved, UnavailableKey);
        if (moved == 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.TargetRejected, pieces, 0,
                "terminal_storage_target_rejected");
        }

        return TerminalExtractionResult.Create(
            moved >= pieces ? TerminalExtractionStatus.Success : TerminalExtractionStatus.Partial,
            pieces, moved, "terminal_storage_extracted");
    }

    /// <summary>
    /// Uses the back counter only in the player's shop. Elsewhere the game can reuse that inventory for another
    /// trader's counter and clear it when the player leaves.
    /// </summary>
    private static IRootSink ClickDestinations(ExtractionPriority priority, TransferOperation operation)
    {
        var store = EmporiumEntry.Instance;
        var sinks = new List<IRootSink>();
        foreach (var destination in ExtractionOrder.For(priority))
        {
            if (destination == ExtractionDestination.Warehouse)
                sinks.Add(new InventoryRootSink(store.invElement, wholeRootOnly: true, operation: operation));
            else if (MapUIManager.Instance == null || !MapUIManager.Instance.IsOutside)
                sinks.Add(new InventoryRootSink(store.backInvinvElement, wholeRootOnly: true, operation: operation));
        }

        return new FallbackRootSink(sinks);
    }

    /// <summary>Moves a display-ordered batch of whole roots into the resolved destination.</summary>
    internal TerminalExtractionResult ExtractBatchTo(GameItem terminal, TerminalDragBatch batch,
        InventoryDropTarget target, TransferOperation operation)
    {
        if (batch == null) throw new ArgumentNullException(nameof(batch));
        if (target == null) throw new ArgumentNullException(nameof(target));
        var requested = batch.RequestedPieces;
        if (requested <= 0 || batch.Steps.Count == 0)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.InvalidAmount, requested, 0,
                "terminal_storage_invalid_amount");

        var mounted = GetInventory(terminal);
        if (!mounted.Network.Online)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Offline, requested, 0, UnavailableKey);

        var sink = new InventoryRootSink(target.Inventory, wholeRootOnly: true,
            pointPlacement: target.ForSinglePlacement(requested), useNativeDragSearch: true,
            containerTarget: target.HoveredItem, itemInventories: target.ItemInventories, operation: operation);
        long moved = 0;
        var status = TerminalExtractionStatus.Success;
        _contentsUncertain = false;
        try
        {
            foreach (var step in batch.Steps)
            {
                var limited = new PieceLimitSink(sink, step.Pieces);
                ExtractResult result;
                try
                {
                    result = mounted.Storage.Extract(step.Key, long.MaxValue, limited, Actionable.Modulate, Player);
                }
                finally
                {
                    moved += limited.Accepted;
                }

                if (result.StoppedBy.Outcome == TransferOutcome.Unowned)
                {
                    _contentsUncertain = true;
                    status = TerminalExtractionStatus.Failed;
                    break;
                }

                // RequestFilled is the limiter's normal stop, not a destination refusal.
                if (limited.Accepted == step.Pieces &&
                    (result.StoppedBy.Outcome == TransferOutcome.SourceEmpty ||
                     result.StoppedBy.Rejection == StoreRejection.RequestFilled))
                    continue;

                status = result.StoppedBy.Outcome == TransferOutcome.SourceEmpty
                    ? TerminalExtractionStatus.NoItems
                    : result.StoppedBy.Outcome == TransferOutcome.Rejected
                        ? TerminalExtractionStatus.TargetRejected
                        : TerminalExtractionStatus.Failed;
                break;
            }
        }
        catch (Exception error)
        {
            _contentsUncertain = true;
            operation.Record(error);
            status = TerminalExtractionStatus.Failed;
        }
        finally
        {
            Settle(mounted);
            foreach (var cell in mounted.Cells) CellPages.Compact(cell, _diagnostics);
        }

        // Earlier successful roots can remain moved, but unknown ownership or an unexpected
        // recovery failure must stay Failed rather than looking like an ordinary partial transfer.
        if (status != TerminalExtractionStatus.Failed && moved > 0 && moved < requested)
            status = TerminalExtractionStatus.Partial;
        else if (status == TerminalExtractionStatus.Success && moved == 0)
            status = TerminalExtractionStatus.NoItems;
        var message = status switch
        {
            TerminalExtractionStatus.Success or TerminalExtractionStatus.Partial => "terminal_storage_extracted",
            TerminalExtractionStatus.NoItems => "terminal_storage_no_items",
            TerminalExtractionStatus.TargetRejected => "terminal_storage_target_rejected",
            _ => UnavailableKey
        };
        return TerminalExtractionResult.Create(status, requested, moved, message);
    }

    /// <summary>Stores or simulates one item; returns the rejection text key, or null when it went in.</summary>
    private string? TryStore(MountedNetwork mounted, GameItem item, Actionable mode, TransferOperation? operation = null)
    {
        if (StoreAdmission.Refusal(item) is { } refusal) return refusal;
        if (Identify(item) is not { } key) return StoreAdmission.UnsupportedKey;
        if (mounted.Cells.Count == 0) return "terminal_storage_reject_no_cell";
        try
        {
            var result = mounted.Storage.Insert(new ItemListSource(new[] { item }, new[] { key }), mode, Player);
            return result.InsertedUnits > 0 ? null : MessageKey(result.StoppedBy, operation);
        }
        catch (Exception error)
        {
            // A verified native move can still fail in the operation's bookkeeping; the item is then
            // stored, and reporting a rejection would be wrong.
            var stored = mode == Actionable.Modulate && IsStoredIn(item, mounted.Cells);
            if (mode == Actionable.Modulate) _viewsStale = true;
            if (operation != null) operation.Record(error);
            else if (mode == Actionable.Modulate)
                _nativeLog($"Digital Storage: store {(stored ? "bookkeeping " : "")}failed ({error.GetType().Name}: {error.Message}).");
            return stored ? null : NoSpaceKey;
        }
    }

    private string MessageKey(TransferStep step, TransferOperation? operation)
    {
        if (step.Outcome == TransferOutcome.Unowned)
        {
            _contentsUncertain = true;
            if (operation != null) operation.RecordUnowned();
            else _nativeLog("Digital Storage: a store left an item without an owner.");
        }
        return step.Rejection switch
        {
            StoreRejection.TypeLimit => "terminal_storage_reject_type_limit",
            StoreRejection.TooLarge => "terminal_storage_reject_too_large",
            StoreRejection.Capacity => "terminal_storage_reject_capacity",
            StoreRejection.DistributionQuota => "terminal_storage_reject_quota",
            StoreRejection.ReadOnly => "terminal_storage_reject_read_only",
            StoreRejection.PartitionRejected => "terminal_storage_reject_no_cell",
            StoreRejection.PartitionUnknown => "terminal_storage_reject_partition_unknown",
            StoreRejection.PartitionInvalid => "terminal_storage_reject_partition_invalid",
            _ => NoSpaceKey
        };
    }

    private bool IsRestoreApplied() => _restoreApplied ??= CellPageRestore.IsApplied();

    /// <summary>
    /// Lists the bays holding a cell on the terminal's grid, numbered by drive order and bay position, for partition
    /// editing. Power is not required: partitions only change cell tags, never stored items.
    /// </summary>
    internal IReadOnlyList<CellBayReference> ListCellBays(GameItem terminal)
    {
        var snapshot = _scanner.Capture(terminal);
        var grid = snapshot.FindGrid(terminal);
        var result = new List<CellBayReference>();
        var drive = 0;
        foreach (var node in grid.Nodes.Where(node => node.Kind == GridNodeKind.Drive))
        {
            drive++;
            var bays = NativeCellResolver.GetBays(snapshot.FindMachine(node));
            for (var index = 0; index < bays.Count; index++)
            {
                if (BasicCellHandler.IsCell(bays[index].childItem))
                {
                    result.Add(new CellBayReference(drive, index + 1, bays[index]));
                }
            }
        }

        return result;
    }

    private Network Connect(GameItem terminal)
    {
        var snapshot = _scanner.Capture(terminal);
        return Connect(snapshot, snapshot.FindGrid(terminal));
    }

    private Network Connect(NativeGridSnapshot snapshot, Grid grid)
    {
        var status = new GridStorage(grid, _isPowered(snapshot, grid)).GetStatus();
        if (status != GridStorageStatus.Online)
        {
            return new Network(false, false, Array.Empty<GameItem>());
        }

        var all = grid.Nodes.Where(node => node.Kind == GridNodeKind.Drive)
            .SelectMany(node => NativeCellResolver.GetCellItems(snapshot.FindMachine(node)))
            .Where(cell => CellPages.Tabs(cell) != null)
            .ToArray();
        var available = all.Where(cell => CellHealth.Get(cell).IsMountable).ToArray();
        return new Network(true, available.Length < all.Length, available);
    }

    private static TerminalEntry Entry(ItemKey key, long pieces, long units, GameItem sample)
    {
        var name = string.IsNullOrWhiteSpace(sample.name) ? sample.identifier : sample.name;
        var sprite = $"{sample.spriteAtlasPath}|{sample.spritePath}";
        var enabledTags = sample.state?.dict == null
            ? Array.Empty<KeyValuePair<string, TagState>>()
            : sample.state.dict.Where(pair => pair.Value != null && pair.Value.valueEnabled).ToArray();
        var tags = enabledTags
            .SelectMany(pair => new[] { pair.Key, pair.Value.identifier, pair.Value.identifierName })
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var document = TerminalSearchMetadata.WithTypeNames(new SearchDocument(name,
            string.Join(" ", new[] { sample.identifier, sample.identifierName }), tags,
            sample.itemTypes, new[] { sample.shortDescription, sample.longDescription, sample.flavorText }),
            enabledTags.Select(pair => pair.Key),
            type => LocHelper.GetLocalizedMechanicOrDefault("type_" + type.ToLowerInvariant(), type));
        return new TerminalEntry(key, pieces, string.IsNullOrWhiteSpace(name) ? "?" : name, sprite,
            Math.Max(1, sample.shape.globalWidth), Math.Max(1, sample.shape.globalHeight), document: document,
            totalUnits: units);
    }

    internal readonly struct Network
    {
        internal Network(bool online, bool storageUnavailable, IReadOnlyList<GameItem> cells)
        {
            Online = online;
            StorageUnavailable = storageUnavailable;
            Cells = cells;
        }

        internal bool Online { get; }

        internal bool StorageUnavailable { get; }

        internal IReadOnlyList<GameItem> Cells { get; }
    }
}

/// <summary>One cell bay found on a grid, with drive and bay display numbers starting at one.</summary>
internal sealed class CellBayReference
{
    internal CellBayReference(int drive, int slot, GameSlotInventory bay)
    {
        Drive = drive;
        Slot = slot;
        Bay = bay;
    }

    internal int Drive { get; }

    internal int Slot { get; }

    internal GameSlotInventory Bay { get; }
}

/// <summary>The storage mounted for one terminal operation.</summary>
internal sealed class MountedNetwork
{
    internal MountedNetwork(StorageService.Network network, NetworkStorage storage,
        IReadOnlyList<(GameItem Cell, BasicCellInventory Inventory)> inventories)
    {
        Network = network;
        Storage = storage;
        Inventories = inventories;
        Cells = inventories.Select(pair => pair.Cell).ToArray();
    }

    internal StorageService.Network Network { get; }

    internal NetworkStorage Storage { get; }

    /// <summary>Gets the mounted cells with the inventory mounted for each.</summary>
    internal IReadOnlyList<(GameItem Cell, BasicCellInventory Inventory)> Inventories { get; }

    /// <summary>Gets the mounted cells.</summary>
    internal IReadOnlyList<GameItem> Cells { get; }
}

/// <summary>Why one item got a fallback key: the reading failure and, for a refused encoding, its rule and place.</summary>
internal readonly struct FallbackReason : IEquatable<FallbackReason>
{
    internal FallbackReason(NativeItemIdentityFailure failure, string? rule, string? at)
    {
        Failure = failure;
        Rule = rule;
        At = at;
    }

    internal NativeItemIdentityFailure Failure { get; }
    internal string? Rule { get; }
    internal string? At { get; }

    public bool Equals(FallbackReason other) => Failure == other.Failure &&
        string.Equals(Rule, other.Rule, StringComparison.Ordinal) && string.Equals(At, other.At, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FallbackReason other && Equals(other);

    public override int GetHashCode() => ((int)Failure * 397) ^ StringComparer.Ordinal.GetHashCode(Rule ?? "") ^
        (StringComparer.Ordinal.GetHashCode(At ?? "") * 31);
}
