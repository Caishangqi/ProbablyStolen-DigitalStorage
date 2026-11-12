using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Api.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Api;

/// <summary>
/// The public <see cref="IStorage"/> of one network, addressed by one of its machines. Every call mounts the live
/// network again, exactly like a terminal call, and moves roots only through the cell transfer and
/// <see cref="InventoryRootSink"/>/<see cref="ItemListSource"/>, so the pages stay the only record of what is stored.
/// Terminals only reread when told: a performed call that moved units reports it through the contents-changed callback,
/// as terminal operations refresh their own window.
/// </summary>
internal sealed class NetworkStorageFacade : IStorage
{
    private readonly StorageService _storage;
    private readonly GameItem _machine;
    private readonly ExternalAccessScope _access;
    private readonly ApiThread _thread;
    private readonly Action<string> _log;
    private readonly Action _contentsChanged;

    internal NetworkStorageFacade(StorageService storage, GameItem machine, ExternalAccessScope access, ApiThread thread,
        Action<string> log, Action contentsChanged)
    {
        _contentsChanged = contentsChanged ?? throw new ArgumentNullException(nameof(contentsChanged));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _thread = thread ?? throw new ArgumentNullException(nameof(thread));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    /// <remarks>A whole network never prefers a type over another network.</remarks>
    public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => false;

    /// <inheritdoc />
    public StorageResult Insert(GameItem root, int units, Actionable mode, IActionSource source)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!TryMount(out var mounted, out var refused)) return refused;
        if (!StorageOutcomes.IsWholeRoot(units, root.unitCount)) return StorageResult.None(StorageStatus.Rejected);
        if (CellPages.CellOfStored(root, out _) != null || !StorageService.IsStorable(root) ||
            StorageService.Identify(root) is not { } key)
        {
            return StorageResult.None(StorageStatus.Rejected);
        }

        try
        {
            var result = mounted!.Storage.Insert(new ItemListSource(new[] { root }, new[] { key }), mode, source);
            if (result.StoppedBy.Outcome == TransferOutcome.Unowned)
                _log($"Digital Storage: API insert left {root.identifier} without an owner.");
            return Report(mode, StorageOutcomes.Inserted(result.InsertedUnits, units));
        }
        catch (Exception error)
        {
            // A verified native move can still fail in the bookkeeping afterwards; the root is then stored.
            var stored = mode == Actionable.Modulate && CellPages.CellOfStored(root, out _) is { } cell &&
                mounted!.Cells.Contains(cell);
            _log($"Digital Storage: API insert {(stored ? "bookkeeping " : "")}failed ({error.GetType().Name}).");
            return Report(mode, stored ? new StorageResult(units, StorageStatus.Done) : StorageResult.None(StorageStatus.Rejected));
        }
    }

    /// <inheritdoc />
    public StorageResult Extract(ItemKey what, long amount, GameGridInventory destination, Actionable mode, IActionSource source)
    {
        if (what == null) throw new ArgumentNullException(nameof(what));
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (amount <= 0) return StorageResult.None(StorageStatus.Rejected);
        if (!TryMount(out var mounted, out var refused)) return refused;

        var sink = new InventoryRootSink(destination);
        long moved;
        var sourceWasEmpty = false;
        try
        {
            var result = mounted!.Storage.Extract(what, amount, sink, mode, source);
            moved = mode == Actionable.Simulate ? result.ExtractedUnits : sink.MovedUnits;
            sourceWasEmpty = result.StoppedBy.Outcome == TransferOutcome.SourceEmpty;
        }
        catch (Exception error)
        {
            moved = sink.MovedUnits;
            _log($"Digital Storage: API extract failed ({error.GetType().Name}: {error.Message}).");
        }
        finally
        {
            if (mode == Actionable.Modulate)
            {
                foreach (var cell in mounted!.Cells) _access.Touched(cell);
            }
        }

        return Report(mode, StorageOutcomes.Extracted(moved, amount, sourceWasEmpty));
    }

    /// <inheritdoc />
    public StorageResult ExtractRoot(GameItem root, int units, GameGridInventory destination, Actionable mode, IActionSource source)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!StorageOutcomes.IsWholeRoot(units, root.unitCount)) return StorageResult.None(StorageStatus.Rejected);
        if (!TryMount(out var mounted, out var refused)) return refused;

        var cell = CellPages.CellOfStored(root, out var page);
        if (cell == null || StorageService.Identify(root) is not { } key) return StorageResult.None(StorageStatus.NotStored);
        if (!mounted!.Cells.Contains(cell))
        {
            // A cell this network skipped because it cannot be read is unavailable; any other cell is elsewhere.
            return StorageResult.None(CellHealth.Get(cell).IsMountable ? StorageStatus.NotStored : StorageStatus.Unavailable);
        }

        var inventory = BasicCellHandler.GetCellInventory(cell, _log);
        if (inventory == null) return StorageResult.None(StorageStatus.Unavailable);

        var sink = new InventoryRootSink(destination);
        long moved;
        var rootWasMissing = false;
        try
        {
            var result = inventory.ExtractRoot(key, page, root.modifiedShape.minX, root.modifiedShape.minY, sink, mode, source);
            rootWasMissing = result.StoppedBy.Outcome == TransferOutcome.SourceEmpty;
            moved = mode == Actionable.Simulate ? (result.StoppedBy.IsMoved ? units : 0) : sink.MovedUnits;
        }
        catch (Exception error)
        {
            moved = sink.MovedUnits;
            _log($"Digital Storage: API root extract failed ({error.GetType().Name}: {error.Message}).");
        }
        finally
        {
            if (mode == Actionable.Modulate) _access.Touched(cell);
        }

        return Report(mode, StorageOutcomes.ExtractedRoot(moved, units, rootWasMissing));
    }

    /// <inheritdoc />
    public StorageStatus GetAvailableStacks(KeyCounter output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (!TryMount(out var mounted, out var refused)) return refused.Status;
        mounted!.Storage.GetAvailableStacks(output);
        return StorageStatus.Done;
    }

    /// <inheritdoc />
    public string GetDescription() => $"digital network of {_machine.identifier}";

    private StorageResult Report(Actionable mode, StorageResult result)
    {
        if (!StorageOutcomes.ChangedContents(mode == Actionable.Simulate, result.Units)) return result;
        try
        {
            _contentsChanged();
        }
        catch (Exception error)
        {
            // The move already happened; a failing listener must not turn it into a reported failure.
            _log($"Digital Storage: contents change notification failed ({error.GetType().Name}).");
        }

        return result;
    }

    private bool TryMount(out MountedNetwork? mounted, out StorageResult refused)
    {
        mounted = null;
        refused = default;
        var early = StorageOutcomes.Refuse(_thread.IsMain, SaveManager.IsDecoding, online: true);
        if (early is { } before)
        {
            refused = before;
            return false;
        }

        mounted = _storage.Mount(_machine);
        if (StorageOutcomes.Refuse(true, false, mounted.Network.Online) is not { } offline) return true;
        refused = offline;
        return false;
    }
}
