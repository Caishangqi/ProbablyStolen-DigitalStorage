using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Application.Queries;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Identity;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Terminal store, extract and read operations over the cells of one live network. Every call scans the network
/// again; items only move through <see cref="CellPages"/>, so the pages stay the only record of what is stored.
/// </summary>
internal sealed class NetworkStorage
{
    internal const string ModItemPrefix = "digital_storage.";
    private const string UnavailableKey = "terminal_storage_unavailable";
    private const string RestoreUnavailableKey = "storage_restore_unavailable";
    private readonly NativeNetworkScanner _scanner;
    private readonly Func<NativeGridSnapshot, Grid, bool> _isPowered;
    private readonly Action<string> _log;
    private readonly Dictionary<ItemKey, GameItem> _samples = new();
    private bool? _restoreApplied;

    internal NetworkStorage(NativeNetworkScanner scanner, Func<NativeGridSnapshot, Grid, bool> isPowered,
        Action<string> log)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _isPowered = isPowered ?? throw new ArgumentNullException(nameof(isPowered));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Writes why one item stayed behind: identifier, units, size and reason.</summary>
    private void LogRejection(GameItem item, StoreRejection rejection) =>
        _log($"Digital Storage: store rejected item={item.identifier} units={item.unitCount} " +
            $"size={CellPages.SizeOf(item)} reason={rejection}");

    private const string UniqueComponent = "digital_storage.unique";
    private static readonly ConditionalWeakTable<GameItem, ItemKey> FallbackKeys = new();
    private static long _nextFallback;

    /// <summary>
    /// Gets an item's type. Items whose full state cannot be read get a fallback key that is unique to that item
    /// for this session: they can still be stored, show as their own terminal entry, and never match a partition.
    /// Mod items get null and are never stored.
    /// </summary>
    internal static ItemKey? Identify(GameItem item)
    {
        if (item == null || IsModItem(item))
        {
            return null;
        }

        var result = NativeItemIdentityReader.Read(item);
        if (result.Status == NativeItemIdentityReadStatus.Success)
        {
            return result.Identity;
        }

        return FallbackKeys.GetValue(item, unread => new ItemKey(
            string.IsNullOrWhiteSpace(unread.identifier) ? "unknown" : unread.identifier,
            new[] { new KeyValuePair<string, string>(UniqueComponent,
                Interlocked.Increment(ref _nextFallback).ToString(CultureInfo.InvariantCulture)) }));
    }

    /// <summary>Reads the aggregated contents of every available cell on the terminal's network.</summary>
    internal TerminalInventorySnapshot Read(GameItem terminal)
    {
        var network = Connect(terminal);
        if (!network.Online)
        {
            return new TerminalInventorySnapshot(false, Array.Empty<TerminalEntry>(), network.StorageUnavailable);
        }

        var groups = new Dictionary<ItemKey, (long Amount, GameItem Sample)>();
        foreach (var root in network.Cells.SelectMany(CellPages.Roots))
        {
            var key = Identify(root);
            if (key == null)
            {
                continue;
            }

            groups[key] = groups.TryGetValue(key, out var group)
                ? (group.Amount + root.unitCount, group.Sample)
                : (root.unitCount, root);
        }

        _samples.Clear();
        foreach (var pair in groups)
        {
            _samples[pair.Key] = pair.Value.Sample;
        }

        var entries = groups.Select(pair => Entry(pair.Key, pair.Value.Amount, pair.Value.Sample));
        return new TerminalInventorySnapshot(true, entries, network.StorageUnavailable);
    }

    /// <summary>
    /// Finds a stored item of a type, used for native tooltips and item pictures. The last read's sample is reused
    /// while it still sits on a storage page; otherwise the network is scanned again.
    /// </summary>
    internal GameItem? FindSample(GameItem terminal, ItemKey key)
    {
        if (_samples.TryGetValue(key, out var cached) && IsStored(cached))
        {
            return cached;
        }

        var network = Connect(terminal);
        return network.Online
            ? network.Cells.SelectMany(CellPages.Roots).FirstOrDefault(root => key.Equals(Identify(root)))
            : null;
    }

    // Storage pages are the only grids this mod insert-locks (CellPages.AddPage).
    private static bool IsStored(GameItem item) =>
        !(item is GameItemElement element && element.IsDestroyed()) &&
        item.parentInventory is GameGridInventory page && page.overrideLockInsert;

    /// <summary>Stores each item independently in selection order; stored items are never rolled back.</summary>
    internal TerminalInsertionResult Store(GameItem terminal, IReadOnlyList<GameItem> items)
    {
        if (!IsRestoreApplied())
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.Offline, 0, RestoreUnavailableKey);
        }

        var network = Connect(terminal);
        if (!network.Online)
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.Offline, 0, UnavailableKey);
        }

        long stored = 0;
        var candidates = 0;
        StoreRejection? firstRejection = null;
        var views = ReadViews(network.Cells);
        foreach (var item in items.Where(item => item != null).Distinct())
        {
            candidates++;
            var units = item.unitCount;
            var rejection = TryStore(network.Cells, views, item);
            if (rejection == null)
            {
                stored += units;
            }
            else
            {
                firstRejection ??= rejection;
                LogRejection(item, rejection.Value);
            }
        }

        if (candidates == 0)
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.NoItems, 0, "terminal_storage_capacity_rejected");
        }

        if (stored == 0)
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.Rejected, 0, MessageKey(firstRejection));
        }

        // Partly stored batches report the stored amount and the first reason the rest stayed behind.
        return TerminalInsertionResult.Create(TerminalInsertionStatus.Success, stored,
            firstRejection == null ? "terminal_storage_inserted" : "terminal_storage_inserted_partial");
    }

    /// <summary>Gets whether one item could be stored right now, without moving anything.</summary>
    internal bool CanStore(GameItem terminal, GameItem item)
    {
        if (item == null || !IsRestoreApplied())
        {
            return false;
        }

        var network = Connect(terminal);
        return network.Online && Plan(network.Cells, ReadViews(network.Cells), item).Cell != null;
    }

    /// <summary>Moves stored items of one type into the player's inventory until at least <paramref name="amount"/> units moved.</summary>
    internal TerminalExtractionResult Extract(GameItem terminal, ItemKey key, long amount)
    {
        if (amount <= 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.InvalidAmount, amount, 0,
                "terminal_storage_invalid_amount");
        }

        var network = Connect(terminal);
        if (!network.Online)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Offline, amount, 0, UnavailableKey);
        }

        var destination = EmporiumEntry.Instance.invElement;
        var roots = network.Cells.SelectMany(CellPages.Roots).Where(root => key.Equals(Identify(root))).ToList();
        if (roots.Count == 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.NoItems, amount, 0, "terminal_storage_no_items");
        }

        long moved = 0;
        foreach (var index in TerminalExtractionPlan.Select(roots.Select(root => (long)root.unitCount).ToArray(), amount))
        {
            var root = roots[index];
            var units = root.unitCount;
            var accepted = CellPages.MoveTo(root, destination);
            moved += accepted;
            if (accepted < units)
            {
                // The inventory is full; the remainder went back to its page.
                break;
            }
        }

        if (moved == 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.TargetRejected, amount, 0,
                "terminal_storage_target_rejected");
        }

        return TerminalExtractionResult.Create(
            moved >= amount ? TerminalExtractionStatus.Success : TerminalExtractionStatus.Partial,
            amount, moved, "terminal_storage_extracted");
    }

    /// <summary>
    /// Rejects mod items and containers holding them: storing a drive or cell inside a cell would nest the
    /// network inside its own storage.
    /// </summary>
    internal static bool IsStorable(GameItem item) =>
        !IsModItem(item) && !GraphUtils.FindAllChildrenType<GameItem>(item).Any(IsModItem);

    private static bool IsModItem(GameItem item) =>
        item.identifier?.StartsWith(ModItemPrefix, StringComparison.Ordinal) == true;

    private static CellView[] ReadViews(IReadOnlyList<GameItem> cells) =>
        cells.Select(cell => CellPages.Read(cell, Identify)).ToArray();

    /// <summary>Stores one item and re-reads only the cell that changed, so a batch reads every cell once.</summary>
    private static StoreRejection? TryStore(IReadOnlyList<GameItem> cells, CellView[] views, GameItem item)
    {
        var plan = Plan(cells, views, item);
        if (plan.Cell == null)
        {
            return plan.Rejection;
        }

        var placed = CellPages.Place(plan.Cell, item, plan.Placement);
        views[plan.Index] = CellPages.Read(plan.Cell, Identify);
        return placed ? null : StoreRejection.NativeRejected;
    }

    private static (GameItem? Cell, int Index, Placement Placement, StoreRejection? Rejection) Plan(
        IReadOnlyList<GameItem> cells, IReadOnlyList<CellView> views, GameItem item)
    {
        var key = IsStorable(item) ? Identify(item) : null;
        if (key == null)
        {
            return (null, -1, default, StoreRejection.Unsupported);
        }

        var footprint = new ItemFootprint(CellPages.SizeOf(item), key);
        StoreRejection? rejection = StoreRejection.NoCell;
        foreach (var index in StorageRouter.Order(views, key))
        {
            var placement = CellPlacementPlanner.Plan(views[index], footprint);
            if (placement.IsAccepted)
            {
                return (cells[index], index, placement, null);
            }

            rejection = placement.Rejection switch
            {
                PlacementRejection.TypeLimit => StoreRejection.TypeLimit,
                PlacementRejection.TooLarge => StoreRejection.TooLarge,
                _ => StoreRejection.NoSpace
            };
        }

        return (null, -1, default, rejection);
    }

    private static string MessageKey(StoreRejection? rejection) => rejection switch
    {
        StoreRejection.Unsupported => "terminal_storage_unsupported_item",
        StoreRejection.NoCell => "terminal_storage_reject_no_cell",
        StoreRejection.TypeLimit => "terminal_storage_reject_type_limit",
        StoreRejection.TooLarge => "terminal_storage_reject_too_large",
        _ => "terminal_storage_reject_no_space"
    };

    private bool IsRestoreApplied() => _restoreApplied ??= CellPageRestore.IsApplied();

    /// <summary>
    /// Gets the native stack limit of a stored type, measured on a stored sample in its page the way the game's own
    /// stacking does (<c>GameItem.MaxNumAdd</c>: the larger inventory or item <c>maxSlotAmountItemFunc</c>, or 1 when
    /// neither is set). Returns 1 when no sample is stored.
    /// </summary>
    internal long StackLimit(GameItem terminal, ItemKey key)
    {
        var sample = FindSample(terminal, key);
        if (sample == null || !IsStored(sample)) return 1;
        var room = sample.MaxNumAdd();
        return room >= int.MaxValue - sample.unitCount ? long.MaxValue : Math.Max(1, sample.unitCount + Math.Max(0, room));
    }

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
                if (NativeCellResolver.IsMountedCell(bays[index].childItem))
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
        var grid = snapshot.FindGrid(terminal);
        var status = new GridStorage(grid, _isPowered(snapshot, grid)).GetStatus();
        if (status != GridStorageStatus.Online)
        {
            return new Network(false, false, Array.Empty<GameItem>());
        }

        var all = grid.Nodes.Where(node => node.Kind == GridNodeKind.Drive)
            .SelectMany(node => NativeCellResolver.GetCellItems(snapshot.FindMachine(node)))
            .Where(cell => CellPages.Tabs(cell) != null)
            .ToArray();
        var available = all.Where(cell => !CellPageAudit.IsUnavailable(cell)).ToArray();
        return new Network(true, available.Length < all.Length, available);
    }

    private static TerminalEntry Entry(ItemKey key, long amount, GameItem sample)
    {
        var name = string.IsNullOrWhiteSpace(sample.name) ? sample.identifier : sample.name;
        var sprite = $"{sample.spriteAtlasPath}|{sample.spritePath}";
        return new TerminalEntry(key, amount, string.IsNullOrWhiteSpace(name) ? "?" : name, sprite,
            Math.Max(1, sample.shape.globalWidth), Math.Max(1, sample.shape.globalHeight));
    }

    private enum StoreRejection
    {
        Unsupported,
        NoCell,
        TypeLimit,
        TooLarge,
        NoSpace,

        // The native placement check (CheckShapePlacement / item state rules) refused the planned slot.
        NativeRejected
    }

    private readonly struct Network
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

/// <summary>One cell bay found on a grid, with 1-based drive and bay numbers for display.</summary>
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
