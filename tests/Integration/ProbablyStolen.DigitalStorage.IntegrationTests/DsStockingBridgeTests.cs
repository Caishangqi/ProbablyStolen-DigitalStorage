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
using ProbablyStolen.DigitalStorage.Compat.DsStocking;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

/// <summary>Exercises DS Stocking Helper runs through the bridge with an in-memory storage network.</summary>
[TestFixture]
public sealed class DsStockingBridgeTests
{
    private FakeNetwork _network = null!;
    private FakeRuntime _runtime = null!;
    private GameGridInventory _page = null!;
    private GameGridInventory _counter = null!;
    private GameItem _drive = null!;
    private GameGridInventory _store = null!;

    [SetUp]
    public void SetUp()
    {
        GraphUtils.NativeTransfers.Clear();
        GraphUtils.NativeRemaining = 0;
        UnityEngine.Debug.Lines.Clear();
        _drive = new GameItem("digital_storage.storage_drive");
        var cell = new GameItem("digital_storage.cell_1k");
        new GameGridInventory { Owner = _drive }.Place(cell);
        _page = new GameGridInventory { Owner = cell };
        _counter = new GameGridInventory();
        _store = new GameGridInventory();
        _network = new FakeNetwork(_drive);
        _runtime = new FakeRuntime(_network);
        DsStockingBridge.Attach(_runtime);
    }

    [TearDown]
    public void TearDown() => DsStockingBridge.Attach(null);

    private GameItem Stored(string id, int units)
    {
        var item = new GameItem(id) { unitCount = units };
        _page.Place(item);
        _network.Stored.Add(item);
        return item;
    }

    private long Units => _network.Stored.Sum(item => item.unitCount) + _counter.childItems.Sum(item => item.unitCount);

    [Test]
    public void OutsideARun_EveryCallStaysNative_AndNoGoodsAreOffered()
    {
        var item = Stored("item.apple", 1);
        GraphUtils.NativeRemaining = 1;

        var remaining = DsStockingBridge.TryAcceptAll(_counter, item, 1);

        Assert.That(remaining, Is.EqualTo(1));
        Assert.That(GraphUtils.NativeTransfers, Has.Count.EqualTo(1));
        Assert.That(_network.Calls, Is.Empty);
        Assert.That(DsStockingBridge.StoredGoods(_store, stacks: true), Is.Empty);
        Assert.That(DsStockingBridge.TryPutBack(item, out _), Is.False);
    }

    [Test]
    public void ARun_IsOneExternalAccessScope()
    {
        DsStockingBridge.BeginRun();
        Assert.That(DsStockingBridge.IsRunning, Is.True);
        Assert.That(_runtime.Access.IsOpen, Is.True, "DS Stocking Helper's removal checks see stored roots.");

        DsStockingBridge.EndRun();

        Assert.That(DsStockingBridge.IsRunning, Is.False);
        Assert.That(_runtime.Access.IsOpen, Is.False);
        Assert.That(_runtime.Access.Opened, Is.EqualTo(1));
    }

    [Test]
    public void InARun_StoredGoodsAreOffered_WithTheDriveAsTheirHolder()
    {
        var apple = Stored("item.apple", 1);
        var pears = Stored("item.pear", 5);

        DsStockingBridge.BeginRun();
        var goods = DsStockingBridge.StoredGoods(_store, stacks: true);
        DsStockingBridge.EndRun();

        Assert.That(goods.Select(entry => entry.Root), Is.EqualTo(new[] { apple, pears }));
        Assert.That(goods.Select(entry => entry.Holder), Is.All.SameAs(_drive));
        Assert.That(_network.Scopes, Is.EqualTo(new[] { _store }));
    }

    [Test]
    public void WithStacksOff_OnlySingleItemsAreOffered()
    {
        var apple = Stored("item.apple", 1);
        Stored("item.pear", 5);

        DsStockingBridge.BeginRun();
        var goods = DsStockingBridge.StoredGoods(_store, stacks: false);
        DsStockingBridge.EndRun();

        Assert.That(goods.Select(entry => entry.Root), Is.EqualTo(new[] { apple }));
    }

    [Test]
    public void AnUnreadableNetwork_OffersNothing_AndIsLogged()
    {
        Stored("item.apple", 1);
        _network.ListStatus = StorageStatus.Unavailable;

        DsStockingBridge.BeginRun();
        var goods = DsStockingBridge.StoredGoods(_store, stacks: true);
        DsStockingBridge.EndRun();

        Assert.That(goods, Is.Empty);
        Assert.That(UnityEngine.Debug.Lines, Has.Some.Contains("Unavailable"));
    }

    [Test]
    public void OneUnitOfAStack_MovesTheWholeRootThroughTheApi()
    {
        var item = Stored("item.pear", 5);
        var before = Units;

        DsStockingBridge.BeginRun();
        var remaining = DsStockingBridge.TryAcceptAll(_counter, item, 1);
        DsStockingBridge.EndRun();

        Assert.That(remaining, Is.Zero, "More units moved than asked, so none are missing.");
        Assert.That(_network.Calls, Is.EqualTo(new[] { "extract item.pear x5" }));
        Assert.That(GraphUtils.NativeTransfers, Is.Empty, "A stored root never moves natively.");
        Assert.That(item.parentInventory, Is.SameAs(_counter));
        Assert.That(Units, Is.EqualTo(before));
    }

    [Test]
    public void SendingBackATakenStack_ReturnsTheWholeRootToItsNetwork()
    {
        var item = Stored("item.pear", 5);
        var before = Units;
        DsStockingBridge.BeginRun();
        DsStockingBridge.TryAcceptAll(_counter, item, 1);

        var remaining = DsStockingBridge.TryAcceptAll(_counter, item, 5);
        DsStockingBridge.EndRun();

        Assert.That(remaining, Is.Zero);
        Assert.That(_network.Calls.Last(), Is.EqualTo("insert item.pear x5"));
        Assert.That(_network.Stored, Does.Contain(item));
        Assert.That(GraphUtils.NativeTransfers, Is.Empty);
        Assert.That(Units, Is.EqualTo(before));
    }

    [Test]
    public void SendingBackPartOfATakenStack_LeavesItOnTheCounter()
    {
        var item = Stored("item.pear", 5);
        DsStockingBridge.BeginRun();
        DsStockingBridge.TryAcceptAll(_counter, item, 1);

        var remaining = DsStockingBridge.TryAcceptAll(_counter, item, 2);
        DsStockingBridge.EndRun();

        Assert.That(remaining, Is.EqualTo(2));
        Assert.That(item.parentInventory, Is.SameAs(_counter));
        Assert.That(_network.Calls, Has.None.StartsWith("insert"));
        Assert.That(GraphUtils.NativeTransfers, Is.Empty);
    }

    [Test]
    public void PuttingBackATakenItem_ReturnsItToItsNetwork_InsteadOfItsOldSlot()
    {
        var item = Stored("item.apple", 1);
        var before = Units;
        DsStockingBridge.BeginRun();
        DsStockingBridge.TryAcceptAll(_counter, item, 1);

        var handled = DsStockingBridge.TryPutBack(item, out var returned);
        DsStockingBridge.EndRun();

        Assert.That(handled, Is.True);
        Assert.That(returned, Is.True);
        Assert.That(_network.Stored, Does.Contain(item));
        Assert.That(Units, Is.EqualTo(before));
    }

    [Test]
    public void PuttingBackAnItemNotTakenThisRun_KeepsDsStockingHelpersOwnReturn()
    {
        var loose = new GameItem("item.apple");
        _counter.Place(loose);

        DsStockingBridge.BeginRun();
        var handled = DsStockingBridge.TryPutBack(loose, out _);
        DsStockingBridge.EndRun();

        Assert.That(handled, Is.False);
        Assert.That(_network.Calls, Is.Empty);
    }

    [Test]
    public void ARefusedReturn_LeavesTheItemOnTheCounter_AndIsLogged()
    {
        var item = Stored("item.apple", 1);
        DsStockingBridge.BeginRun();
        DsStockingBridge.TryAcceptAll(_counter, item, 1);
        _network.RefuseInsert = true;

        DsStockingBridge.TryPutBack(item, out var returned);
        var remaining = DsStockingBridge.TryAcceptAll(_counter, item, 1);
        DsStockingBridge.EndRun();

        Assert.That(returned, Is.False);
        Assert.That(remaining, Is.EqualTo(1));
        Assert.That(item.parentInventory, Is.SameAs(_counter));
        Assert.That(GraphUtils.NativeTransfers, Is.Empty);
        Assert.That(UnityEngine.Debug.Lines, Has.Some.Contains("could not return"));
    }

    [Test]
    public void AMergedStackSentBackToASourcePage_NeverMovesNatively()
    {
        var item = Stored("item.pear", 2);
        var merged = new GameItem("item.pear") { unitCount = 5 };
        DsStockingBridge.BeginRun();
        DsStockingBridge.TryAcceptAll(_counter, item, 1);
        _counter.Place(merged);

        var remaining = DsStockingBridge.TryAcceptAll(_page, merged, 2);
        DsStockingBridge.EndRun();

        Assert.That(remaining, Is.EqualTo(2));
        Assert.That(GraphUtils.NativeTransfers, Is.Empty);
    }

    [Test]
    public void ItemsOutsideTheNetwork_StayNativeInARun()
    {
        var loose = new GameItem("item.apple") { unitCount = 2 };
        new GameGridInventory().Place(loose);

        DsStockingBridge.BeginRun();
        var remaining = DsStockingBridge.TryAcceptAll(_counter, loose, 1);
        DsStockingBridge.EndRun();

        Assert.That(remaining, Is.Zero);
        Assert.That(GraphUtils.NativeTransfers, Has.Count.EqualTo(1));
        Assert.That(_network.Calls, Is.Empty);
    }

    [Test]
    public void AnOfflineNetwork_MovesNothing()
    {
        var item = Stored("item.apple", 1);
        _network.Offline = true;

        DsStockingBridge.BeginRun();
        var remaining = DsStockingBridge.TryAcceptAll(_counter, item, 1);
        DsStockingBridge.EndRun();

        Assert.That(remaining, Is.EqualTo(1));
        Assert.That(item.parentInventory, Is.SameAs(_page));
        Assert.That(GraphUtils.NativeTransfers, Is.Empty);
    }

    [Test]
    public void AThrowingNetwork_MovesNothing_AndLogs()
    {
        var item = Stored("item.apple", 1);
        _network.Throw = true;

        DsStockingBridge.BeginRun();
        var remaining = DsStockingBridge.TryAcceptAll(_counter, item, 1);
        DsStockingBridge.EndRun();

        Assert.That(remaining, Is.EqualTo(1));
        Assert.That(item.parentInventory, Is.SameAs(_page));
        Assert.That(UnityEngine.Debug.Lines, Has.Some.Contains("failed"));
        Assert.That(_runtime.Access.IsOpen, Is.False);
    }

    [Test]
    public void TakenRoots_DoNotOutliveTheRun()
    {
        var item = Stored("item.apple", 1);
        DsStockingBridge.BeginRun();
        DsStockingBridge.TryAcceptAll(_counter, item, 1);
        DsStockingBridge.EndRun();
        GraphUtils.NativeRemaining = 1;

        DsStockingBridge.BeginRun();
        var handled = DsStockingBridge.TryPutBack(item, out _);
        var remaining = DsStockingBridge.TryAcceptAll(_page, item, 1);
        DsStockingBridge.EndRun();

        Assert.That(handled, Is.False, "An item on the counter after its run belongs to the player.");
        Assert.That(remaining, Is.EqualTo(1));
        Assert.That(_network.Calls, Has.None.StartsWith("insert"));
    }

    [Test]
    public void WithoutARuntime_ARunChangesNothing()
    {
        DsStockingBridge.Attach(null);

        DsStockingBridge.BeginRun();

        Assert.That(DsStockingBridge.IsRunning, Is.False);
        Assert.That(DsStockingBridge.StoredGoods(_store, stacks: true), Is.Empty);
    }

    private sealed class FakeNetwork : IStorage, IStorageService, IGrid, IGridNode
    {
        internal FakeNetwork(GameItem owner) => Owner = owner;

        internal List<GameItem> Stored { get; } = new();

        internal List<string> Calls { get; } = new();

        internal List<GraphNodeStorage> Scopes { get; } = new();

        internal bool Offline { get; set; }

        internal bool Throw { get; set; }

        internal bool RefuseInsert { get; set; }

        internal StorageStatus ListStatus { get; set; } = StorageStatus.Done;

        public bool IsOnline => !Offline;

        public IGrid Grid => this;

        public GameItem Owner { get; }

        public IStorageService GetStorageService() => this;

        public IStorage GetInventory() => this;

        public KeyCounter? GetCachedInventory() => new();

        public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => false;

        public StorageResult Insert(GameItem root, int units, Actionable mode, IActionSource source)
        {
            Assert.That(source.ModId, Is.EqualTo("ds_stocking"));
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
            Assert.That(units, Is.EqualTo(root.unitCount), "The API never splits a stack.");
            if (Throw) throw new InvalidOperationException("boom");
            if (Offline) return StorageResult.None(StorageStatus.Unavailable);
            if (!Stored.Contains(root)) return StorageResult.None(StorageStatus.NotStored);
            Calls.Add($"extract {root.identifier} x{units}");
            Stored.Remove(root);
            destination.Place(root);
            return new StorageResult(root.unitCount, StorageStatus.Done);
        }

        public StorageStatus GetAvailableStacks(KeyCounter output) => StorageStatus.Done;

        public StorageStatus GetStoredRoots(ICollection<GameItem> output)
        {
            if (ListStatus != StorageStatus.Done) return ListStatus;
            foreach (var root in Stored) output.Add(root);
            return StorageStatus.Done;
        }

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

        public IReadOnlyList<IGrid> FindOnlineGrids(GraphNodeStorage scope)
        {
            _network.Scopes.Add(scope);
            return _network.Offline ? Array.Empty<IGrid>() : new IGrid[] { _network };
        }
    }

    private sealed class FakeAccess : IExternalAccess
    {
        private int _depth;

        internal int Opened { get; private set; }

        public bool IsOpen => _depth > 0;

        public IDisposable Open(IActionSource source)
        {
            Assert.That(source.ModId, Is.EqualTo("ds_stocking"));
            _depth++;
            Opened++;
            return new Closer(() => _depth--);
        }

        public bool IsBatching => false;

        public IDisposable Batch(IActionSource source) =>
            throw new NotSupportedException("A synchronous run needs no batch.");

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
        internal FakeRuntime(FakeNetwork network)
        {
            Grids = new FakeGrids(network);
        }

        internal FakeAccess Access { get; } = new();

        public IGridHelper Grids { get; }

        public IExternalAccess ExternalAccess => Access;

        public ProbablyStolen.DigitalStorage.Api.Client.ISlotMetricsProvider SlotMetrics => throw new NotSupportedException();
    }
}
