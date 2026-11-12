using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Networking;
using ProbablyStolen.DigitalStorage.Api.Networking.Storage;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Api.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Queries;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Api;

/// <summary>
/// The public <see cref="IGridHelper"/>. Networks are found the same way as for terminals: one scan of the machines in
/// the native grid that holds the asked machine. Views record the online state at lookup time.
/// </summary>
internal sealed class GridHelper : IGridHelper
{
    private readonly NativeNetworkScanner _scanner;
    private readonly Func<NativeGridSnapshot, Grid, bool> _isPowered;
    private readonly Func<GameItem, IStorage> _storageOf;

    /// <param name="scanner">The network scanner.</param>
    /// <param name="isPowered">Whether a scanned grid has controller power.</param>
    /// <param name="storageOf">Creates the public storage of the network a machine belongs to.</param>
    internal GridHelper(NativeNetworkScanner scanner, Func<NativeGridSnapshot, Grid, bool> isPowered,
        Func<GameItem, IStorage> storageOf)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _isPowered = isPowered ?? throw new ArgumentNullException(nameof(isPowered));
        _storageOf = storageOf ?? throw new ArgumentNullException(nameof(storageOf));
    }

    /// <inheritdoc />
    public bool TryGetNode(GameItem item, out IGridNode? node)
    {
        node = null;
        if (item == null || !NativeNetworkScanner.IsNetworkMachine(item)) return false;
        var snapshot = _scanner.Capture(item);
        var grid = snapshot.FindGrid(item);
        node = new NodeView(item, new GridView(item, IsOnline(snapshot, grid), _storageOf));
        return true;
    }

    /// <inheritdoc />
    public bool TryGetNodeOfStored(GameItem storedRoot, out IGridNode? node)
    {
        node = null;
        var cell = CellPages.CellOfStored(storedRoot, out _);
        // A mounted cell sits directly in a drive bay; the bay's parent item is the drive.
        var drive = cell?.parentInventory?.GetParentItem();
        return drive != null && drive.identifier == DriveItem.ItemId && TryGetNode(drive, out node);
    }

    /// <inheritdoc />
    public IReadOnlyList<IGrid> FindOnlineGrids(GraphNodeStorage scope)
    {
        if (scope == null) throw new ArgumentNullException(nameof(scope));
        var result = new List<IGrid>();
        var covered = new HashSet<GameItem>();
        foreach (var drive in GraphUtils.FindAllChildrenType<GameItem>(scope, item => item.identifier == DriveItem.ItemId))
        {
            if (covered.Contains(drive)) continue;
            var snapshot = _scanner.Capture(drive);
            var grid = snapshot.FindGrid(drive);
            foreach (var machine in grid.Nodes.Select(snapshot.FindMachine)) covered.Add(machine);
            if (IsOnline(snapshot, grid)) result.Add(new GridView(drive, true, _storageOf));
        }

        return result;
    }

    private bool IsOnline(NativeGridSnapshot snapshot, Grid grid) =>
        new GridStorage(grid, _isPowered(snapshot, grid)).GetStatus() == GridStorageStatus.Online;

    private sealed class GridView : IGrid, IStorageService
    {
        private readonly GameItem _machine;
        private readonly Func<GameItem, IStorage> _storageOf;

        internal GridView(GameItem machine, bool online, Func<GameItem, IStorage> storageOf)
        {
            _machine = machine;
            IsOnline = online;
            _storageOf = storageOf;
        }

        public bool IsOnline { get; }

        public IStorageService GetStorageService() => this;

        public IStorage GetInventory() => _storageOf(_machine);

        public KeyCounter? GetCachedInventory()
        {
            var counter = new KeyCounter();
            return GetInventory().GetAvailableStacks(counter) == StorageStatus.Done ? counter : null;
        }
    }

    private sealed class NodeView : IGridNode
    {
        internal NodeView(GameItem owner, IGrid grid)
        {
            Owner = owner;
            Grid = grid;
        }

        public IGrid Grid { get; }

        public GameItem Owner { get; }

        public bool IsOnline => Grid.IsOnline;
    }
}
