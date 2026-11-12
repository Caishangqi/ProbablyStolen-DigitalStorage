using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Networking.Storage;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Api.Storage;
using ProbablyStolen.DigitalStorage.Compat.CustomerStocking;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

/// <summary>
/// The Customer Stocking bridge with a network double. Harmony and Customer Stocking are not loaded: the transpiler's
/// rewrite count is checked by the plugin at runtime and covered by in-game acceptance.
/// </summary>
[TestFixture]
public sealed class StockingBridgeTests
{
    private FakeNetwork _network = null!;
    private FakeRuntime _runtime = null!;
    private GameGridInventory _page = null!;
    private GameGridInventory _counter = null!;

    [SetUp]
    public void SetUp()
    {
        GraphUtils.NativeTransfers.Clear();
        GraphUtils.NativeRemaining = 0;
        UnityEngine.Debug.Lines.Clear();
        _page = new GameGridInventory();
        _counter = new GameGridInventory();
        _network = new FakeNetwork();
        _runtime = new FakeRuntime(_network);
        StockingBridge.Attach(_runtime);
    }

    [TearDown]
    public void TearDown() => StockingBridge.Attach(null);

    private GameItem Stored(string id, int units)
    {
        var item = new GameItem(id) { unitCount = units };
        _page.Place(item);
        _network.Stored.Add(item);
        return item;
    }

    private long Units => _network.Stored.Sum(item => item.unitCount) + _counter.childItems.Sum(item => item.unitCount);

    [Test]
    public void WithoutASession_EveryCallStaysNative()
    {
        var item = Stored("item.apple", 3);
        GraphUtils.NativeRemaining = 3;

        var remaining = StockingBridge.TryAcceptAll(_counter, item, 3);

        Assert.That(remaining, Is.EqualTo(3));
        Assert.That(GraphUtils.NativeTransfers, Has.Count.EqualTo(1));
        Assert.That(_network.Calls, Is.Empty);
    }

    [Test]
    public void InASession_AStoredRootMovesThroughTheApi_AndTheRemainingCountIsReturned()
    {
        var item = Stored("item.apple", 3);
        var before = Units;

        var session = StockingBridge.OpenSession();
        var remaining = StockingBridge.TryAcceptAll(_counter, item, -1);
        StockingBridge.CloseSession(session);

        Assert.That(remaining, Is.Zero, "TryAcceptAll returns the units that did not move.");
        Assert.That(_network.Calls, Is.EqualTo(new[] { "extract item.apple x3" }));
        Assert.That(GraphUtils.NativeTransfers, Is.Empty, "A stored root never moves natively.");
        Assert.That(item.parentInventory, Is.SameAs(_counter));
        Assert.That(Units, Is.EqualTo(before));
        Assert.That(_runtime.Access.Opened, Is.EqualTo(1));
        Assert.That(_runtime.Access.IsOpen, Is.False);
    }

    [Test]
    public void BudgetRollback_ReturnsAWholeRootToItsNetwork()
    {
        var item = Stored("item.apple", 4);
        var before = Units;
        var session = StockingBridge.OpenSession();
        StockingBridge.TryAcceptAll(_counter, item, 4);

        var remaining = StockingBridge.TryAcceptAll(_page, item, 4);
        StockingBridge.CloseSession(session);

        Assert.That(remaining, Is.Zero);
        Assert.That(_network.Calls.Last(), Is.EqualTo("insert item.apple x4"));
        Assert.That(_network.Stored, Does.Contain(item));
        Assert.That(Units, Is.EqualTo(before));
    }

    [Test]
    public void PartialRollback_LeavesTheItemOnTheCounter()
    {
        var item = Stored("item.apple", 4);
        var session = StockingBridge.OpenSession();
        StockingBridge.TryAcceptAll(_counter, item, 4);

        var remaining = StockingBridge.TryAcceptAll(_page, item, 2);
        StockingBridge.CloseSession(session);

        Assert.That(remaining, Is.EqualTo(2), "Nothing moved, so Customer Stocking reports that it could not roll back.");
        Assert.That(item.parentInventory, Is.SameAs(_counter));
        Assert.That(_network.Calls, Has.None.StartsWith("insert"));
    }

    [Test]
    public void RefusedReturn_LeavesTheItemOnTheCounter()
    {
        var item = Stored("item.apple", 4);
        var session = StockingBridge.OpenSession();
        StockingBridge.TryAcceptAll(_counter, item, 4);
        _network.RefuseInsert = true;

        var remaining = StockingBridge.TryAcceptAll(_page, item, 4);
        StockingBridge.CloseSession(session);

        Assert.That(remaining, Is.EqualTo(4));
        Assert.That(item.parentInventory, Is.SameAs(_counter));
        Assert.That(UnityEngine.Debug.Lines, Has.Some.Contains("could not return"));
    }

    [Test]
    public void ItemsOutsideTheNetwork_StayNativeInASession()
    {
        var loose = new GameItem("item.pear") { unitCount = 2 };
        new GameGridInventory().Place(loose);
        GraphUtils.NativeRemaining = 0;

        var session = StockingBridge.OpenSession();
        var remaining = StockingBridge.TryAcceptAll(_counter, loose, 2);
        StockingBridge.CloseSession(session);

        Assert.That(remaining, Is.Zero);
        Assert.That(GraphUtils.NativeTransfers, Has.Count.EqualTo(1));
        Assert.That(_network.Calls, Is.Empty);
    }

    [Test]
    public void OfflineNetwork_MovesNothing()
    {
        var item = Stored("item.apple", 3);
        _network.Offline = true;

        var session = StockingBridge.OpenSession();
        var remaining = StockingBridge.TryAcceptAll(_counter, item, 3);
        StockingBridge.CloseSession(session);

        Assert.That(remaining, Is.EqualTo(3));
        Assert.That(item.parentInventory, Is.SameAs(_page));
        Assert.That(GraphUtils.NativeTransfers, Is.Empty);
    }

    [Test]
    public void ThrowingStorage_MovesNothing_AndLogs()
    {
        var item = Stored("item.apple", 3);
        _network.Throw = true;

        var session = StockingBridge.OpenSession();
        var remaining = StockingBridge.TryAcceptAll(_counter, item, 3);
        StockingBridge.CloseSession(session);

        Assert.That(remaining, Is.EqualTo(3));
        Assert.That(item.parentInventory, Is.SameAs(_page));
        Assert.That(UnityEngine.Debug.Lines, Has.Some.Contains("failed"));
        Assert.That(_runtime.Access.IsOpen, Is.False);
    }

    [Test]
    public void RollbackPages_DoNotOutliveTheSession()
    {
        var item = Stored("item.apple", 4);
        var session = StockingBridge.OpenSession();
        StockingBridge.TryAcceptAll(_counter, item, 4);
        StockingBridge.CloseSession(session);
        GraphUtils.NativeRemaining = 4;

        var next = StockingBridge.OpenSession();
        var remaining = StockingBridge.TryAcceptAll(_page, item, 4);
        StockingBridge.CloseSession(next);

        Assert.That(remaining, Is.EqualTo(4));
        Assert.That(GraphUtils.NativeTransfers, Has.Count.EqualTo(1), "A page from an earlier session is not ours to return to.");
    }

    private sealed class FakeNetwork : IStorage, IStorageService, IGrid, IGridNode
    {
        internal List<GameItem> Stored { get; } = new();

        internal List<string> Calls { get; } = new();

        internal bool Offline { get; set; }

        internal bool Throw { get; set; }

        internal bool RefuseInsert { get; set; }

        public bool IsOnline => !Offline;

        public IGrid Grid => this;

        public GameItem Owner { get; } = new("digital_storage.storage_drive");

        public IStorageService GetStorageService() => this;

        public IStorage GetInventory() => this;

        public KeyCounter? GetCachedInventory() => new();

        public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => false;

        public StorageResult Insert(GameItem root, int units, Actionable mode, IActionSource source)
        {
            Assert.That(source.ModId, Is.EqualTo("customerstocking"));
            if (Offline) return StorageResult.None(StorageStatus.Unavailable);
            if (RefuseInsert || units != root.unitCount) return StorageResult.None(StorageStatus.Rejected);
            Calls.Add($"insert {root.identifier} x{units}");
            Stored.Add(root);
            root.parentInventory?.childItems.Remove(root);
            root.parentInventory = null;
            return new StorageResult(units, StorageStatus.Done);
        }

        public StorageResult Extract(ItemKey what, long amount, GameGridInventory destination, Actionable mode,
            IActionSource source) => throw new NotSupportedException("The bridge only moves chosen roots.");

        public StorageResult ExtractRoot(GameItem root, int units, GameGridInventory destination, Actionable mode,
            IActionSource source)
        {
            Assert.That(mode, Is.EqualTo(Actionable.Modulate));
            if (Throw) throw new InvalidOperationException("boom");
            if (Offline) return StorageResult.None(StorageStatus.Unavailable);
            if (!Stored.Contains(root)) return StorageResult.None(StorageStatus.NotStored);
            Calls.Add($"extract {root.identifier} x{units}");
            Stored.Remove(root);
            destination.Place(root);
            return new StorageResult(root.unitCount, StorageStatus.Done);
        }

        public StorageStatus GetAvailableStacks(KeyCounter output) => StorageStatus.Done;

        public string GetDescription() => "fake network";
    }

    private sealed class FakeGrids : IGridHelper
    {
        private readonly FakeNetwork _network;

        internal FakeGrids(FakeNetwork network) => _network = network;

        public bool TryGetNode(GameItem item, out IGridNode? node)
        {
            node = null;
            return false;
        }

        public bool TryGetNodeOfStored(GameItem storedRoot, out IGridNode? node)
        {
            node = _network.Stored.Contains(storedRoot) ? _network : null;
            return node != null;
        }

        public IReadOnlyList<IGrid> FindOnlineGrids(GraphNodeStorage scope) => Array.Empty<IGrid>();
    }

    private sealed class FakeAccess : IExternalAccess
    {
        private int _depth;

        internal int Opened { get; private set; }

        public bool IsOpen => _depth > 0;

        public IDisposable Open(IActionSource source)
        {
            Assert.That(source.ModId, Is.EqualTo("customerstocking"));
            _depth++;
            Opened++;
            return new Closer(() => _depth--);
        }

        private sealed class Closer : IDisposable
        {
            private Action? _close;

            internal Closer(Action close) => _close = close;

            public void Dispose()
            {
                _close?.Invoke();
                _close = null;
            }
        }
    }

    private sealed class FakeRuntime : IDigitalStorageRuntime
    {
        internal FakeRuntime(FakeNetwork network) => Grids = new FakeGrids(network);

        internal FakeAccess Access { get; } = new();

        public IGridHelper Grids { get; }

        public IExternalAccess ExternalAccess => Access;
    }
}
