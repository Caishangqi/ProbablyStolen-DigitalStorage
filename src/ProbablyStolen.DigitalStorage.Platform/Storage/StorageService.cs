using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Queries;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
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
internal sealed class StorageService
{
    internal const string ModItemPrefix = "digital_storage.";
    private const string UnavailableKey = "terminal_storage_unavailable";
    private const string RestoreUnavailableKey = "storage_restore_unavailable";
    private readonly NativeNetworkScanner _scanner;
    private readonly Func<NativeGridSnapshot, Grid, bool> _isPowered;
    private readonly Action<string> _log;
    private const string NoSpaceKey = "terminal_storage_reject_no_space";
    private static readonly IActionSource Player = new PlayerSource("player");
    private readonly Func<GameItem, bool> _migrate;
    private bool _viewsStale;
    private readonly Dictionary<ItemKey, GameItem> _samples = new();
    private bool? _restoreApplied;

    internal StorageService(NativeNetworkScanner scanner, Func<NativeGridSnapshot, Grid, bool> isPowered,
        Action<string> log, Func<GameItem, bool> migrate)
    {
        _migrate = migrate ?? throw new ArgumentNullException(nameof(migrate));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _isPowered = isPowered ?? throw new ArgumentNullException(nameof(isPowered));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

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
    internal TerminalInventorySnapshot GetCachedInventory(GameItem terminal)
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
    private static bool IsStoredIn(GameItem item, IReadOnlyList<GameItem> cells) =>
        !(item is GameItemElement element && element.IsDestroyed()) && item.parentInventory is GameGridInventory page &&
        cells.Any(cell => CellPages.Pages(cell).Contains(page));

    private static bool IsStored(GameItem item) =>
        !(item is GameItemElement element && element.IsDestroyed()) &&
        item.parentInventory is GameGridInventory page && page.overrideLockInsert;

    /// <summary>
    /// Mounts every usable cell of the terminal's network. Pending schema 1
    /// cells, such as cells assembled during this session, are migrated first. Locked and unavailable cells are not
    /// mounted; extract-only cells are mounted without insertion.
    /// </summary>
    internal MountedNetwork GetInventory(GameItem terminal) => GetInventory(terminal, migratePending: true);

    // Simulations pass false: a read-only check must never move items, even through a lazy migration.
    private MountedNetwork GetInventory(GameItem terminal, bool migratePending)
    {
        var network = Connect(terminal);
        var storage = new NetworkStorage();
        var cells = new List<GameItem>();
        if (!network.Online) return new MountedNetwork(network, storage, cells);

        foreach (var cell in network.Cells)
        {
            if (migratePending) _migrate(cell);
            var inventory = BasicCellHandler.GetCellInventory(cell, _log);
            if (inventory == null) continue;
            var readOnly = CellHealth.Get(cell).Status == CellHealthStatus.ReadOnly;
            storage.Mount(inventory.View.Priority,
                new InventoryHandler(inventory, inventory.View.Partition, allowInsertion: !readOnly));
            cells.Add(cell);
        }

        return new MountedNetwork(network, storage, cells);
    }

    /// <summary>Stores each item independently in selection order; stored items are never rolled back.</summary>
    internal TerminalInsertionResult Store(GameItem terminal, IReadOnlyList<GameItem> items)
    {
        if (!IsRestoreApplied())
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.Offline, 0, RestoreUnavailableKey);
        }

        var mounted = GetInventory(terminal);
        if (!mounted.Network.Online)
        {
            return TerminalInsertionResult.Create(TerminalInsertionStatus.Offline, 0, UnavailableKey);
        }

        long stored = 0;
        var candidates = 0;
        string? firstRejection = null;
        foreach (var item in items.Where(item => item != null).Distinct())
        {
            candidates++;
            var units = item.unitCount;
            if (_viewsStale)
            {
                mounted = GetInventory(terminal, migratePending: false);
                _viewsStale = false;
            }

            var rejection = TryStore(mounted, item, Actionable.Modulate);
            if (rejection == null)
            {
                stored += units;
            }
            else
            {
                firstRejection ??= rejection;
                _log($"Digital Storage: store rejected item={item.identifier} units={item.unitCount} " +
                    $"size={CellPages.SizeOf(item)} reason={rejection}");
            }
        }

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

    /// <summary>Gets whether one item could be stored right now, without moving anything.</summary>
    internal bool CanStore(GameItem terminal, GameItem item)
    {
        if (item == null || !IsRestoreApplied())
        {
            return false;
        }

        var mounted = GetInventory(terminal, migratePending: false);
        return mounted.Network.Online && TryStore(mounted, item, Actionable.Simulate) == null;
    }

    /// <summary>Moves stored items of one type into the player's inventory until at least <paramref name="amount"/> units moved.</summary>
    internal TerminalExtractionResult Extract(GameItem terminal, ItemKey key, long amount)
    {
        if (amount <= 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.InvalidAmount, amount, 0,
                "terminal_storage_invalid_amount");
        }

        var mounted = GetInventory(terminal);
        if (!mounted.Network.Online)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Offline, amount, 0, UnavailableKey);
        }

        var available = new KeyCounter();
        mounted.Storage.GetAvailableStacks(available);
        if (available.Get(key) == 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.NoItems, amount, 0, "terminal_storage_no_items");
        }

        // Extraction starts at the lowest priority, so higher-priority drives keep their items longest.
        var destination = new InventoryRootSink(EmporiumEntry.Instance.invElement);
        try
        {
            var result = mounted.Storage.Extract(key, amount, destination, Actionable.Modulate, Player);
            if (result.StoppedBy.Outcome == TransferOutcome.Unowned)
                _log($"Digital Storage: extract left an item without an owner (key={key.Identifier}).");
        }
        catch (Exception error)
        {
            _log($"Digital Storage: extract failed ({error.GetType().Name}: {error.Message}).");
        }
        finally
        {
            foreach (var cell in mounted.Cells) CellPages.Compact(cell);
        }

        var moved = destination.MovedUnits;
        if (moved == 0)
        {
            return TerminalExtractionResult.Create(TerminalExtractionStatus.TargetRejected, amount, 0,
                "terminal_storage_target_rejected");
        }

        return TerminalExtractionResult.Create(
            moved >= amount ? TerminalExtractionStatus.Success : TerminalExtractionStatus.Partial,
            amount, moved, "terminal_storage_extracted");
    }

    /// <summary>Stores or simulates one item; returns the rejection text key, or null when it went in.</summary>
    private string? TryStore(MountedNetwork mounted, GameItem item, Actionable mode)
    {
        var key = IsStorable(item) ? Identify(item) : null;
        if (key == null) return "terminal_storage_unsupported_item";
        if (mounted.Cells.Count == 0) return "terminal_storage_reject_no_cell";
        try
        {
            var result = mounted.Storage.Insert(new ItemListSource(new[] { item }, new[] { key }), mode, Player);
            return result.InsertedUnits > 0 ? null : MessageKey(result.StoppedBy);
        }
        catch (Exception error)
        {
            // A verified native move can still fail afterwards in the per-operation bookkeeping; the item is then
            // stored, and reporting a rejection would be wrong.
            var stored = mode == Actionable.Modulate && IsStoredIn(item, mounted.Cells);
            _viewsStale = true;
            _log($"Digital Storage: store {(stored ? "bookkeeping " : "")}failed ({error.GetType().Name}: {error.Message}).");
            return stored ? null : NoSpaceKey;
        }
    }

    private string MessageKey(TransferStep step)
    {
        if (step.Outcome == TransferOutcome.Unowned)
            _log("Digital Storage: a store left an item without an owner.");
        return step.Rejection switch
        {
            StoreRejection.TypeLimit => "terminal_storage_reject_type_limit",
            StoreRejection.TooLarge => "terminal_storage_reject_too_large",
            StoreRejection.Capacity => "terminal_storage_reject_capacity",
            StoreRejection.DistributionQuota => "terminal_storage_reject_quota",
            StoreRejection.ReadOnly => "terminal_storage_reject_read_only",
            StoreRejection.PartitionRejected => "terminal_storage_reject_no_cell",
            _ => NoSpaceKey
        };
    }

    /// <summary>
    /// Rejects mod items and containers holding them: storing a drive or cell inside a cell would nest the
    /// network inside its own storage.
    /// </summary>
    internal static bool IsStorable(GameItem item) =>
        !IsModItem(item) && !GraphUtils.FindAllChildrenType<GameItem>(item).Any(IsModItem);

    private static bool IsModItem(GameItem item) =>
        item.identifier?.StartsWith(ModItemPrefix, StringComparison.Ordinal) == true;

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
        var available = all.Where(cell => CellHealth.Get(cell).IsMountable).ToArray();
        return new Network(true, available.Length < all.Length, available);
    }

    private static TerminalEntry Entry(ItemKey key, long amount, GameItem sample)
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
        return new TerminalEntry(key, amount, string.IsNullOrWhiteSpace(name) ? "?" : name, sprite,
            Math.Max(1, sample.shape.globalWidth), Math.Max(1, sample.shape.globalHeight), document: document);
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

/// <summary>The storage mounted for one terminal operation.</summary>
internal sealed class MountedNetwork
{
    internal MountedNetwork(StorageService.Network network, NetworkStorage storage, IReadOnlyList<GameItem> cells)
    {
        Network = network;
        Storage = storage;
        Cells = cells;
    }

    internal StorageService.Network Network { get; }

    internal NetworkStorage Storage { get; }

    /// <summary>Gets the mounted cells.</summary>
    internal IReadOnlyList<GameItem> Cells { get; }
}
