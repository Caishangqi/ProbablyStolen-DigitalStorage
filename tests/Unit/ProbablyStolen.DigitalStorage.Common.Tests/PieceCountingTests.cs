using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class PieceCountingTests
{
    private static readonly BoundingSize OneByThree = new(1, 3);
    private static readonly IActionSource Player = new PlayerSource("tester");
    private static readonly ItemKey Fuel = new("exotic_fuel");

    [Test]
    public void Pieces_CountRootsWhateverTheirUnits()
    {
        var cell = NewCell();
        cell.Insert(new MemoryInventory().With(OneByThree, "exotic_fuel", 13).With(OneByThree, "exotic_fuel", 50)
            .With(OneByThree, "exotic_fuel", 7), Actionable.Modulate, Player);

        Assert.That(Pieces(cell).Get(Fuel), Is.EqualTo(3));
        Assert.That(Units(cell).Get(Fuel), Is.EqualTo(70));
    }

    [Test]
    public void Pieces_DropByOnePerRootLeaving()
    {
        var cell = NewCell();
        cell.Insert(new MemoryInventory().With(OneByThree, "exotic_fuel", 13).With(OneByThree, "exotic_fuel", 50),
            Actionable.Modulate, Player);

        cell.Extract(Fuel, 1, new MemoryInventory(), Actionable.Modulate, Player);

        Assert.That(Pieces(cell).Get(Fuel), Is.EqualTo(1));
    }

    [Test]
    public void PieceLimitSink_StopsAfterTheRequestedRoots()
    {
        var cell = NewCell();
        var source = new MemoryInventory();
        for (var i = 0; i < 5; i++) source.With(OneByThree, "exotic_fuel", 10 + i);
        cell.Insert(source, Actionable.Modulate, Player);
        var bag = new MemoryInventory();
        var limited = new PieceLimitSink(bag, 2);

        var result = cell.Extract(Fuel, long.MaxValue, limited, Actionable.Modulate, Player);

        Assert.That(limited.Accepted, Is.EqualTo(2));
        Assert.That(bag.Roots, Has.Count.EqualTo(2));
        Assert.That(result.StoppedBy.Rejection, Is.EqualTo(StoreRejection.RequestFilled));
        Assert.That(Pieces(cell).Get(Fuel), Is.EqualTo(3));
    }

    [Test]
    public void PieceLimitSink_ThroughNetworkStorage_StopsEveryCell()
    {
        var first = NewCell();
        var second = NewCell();
        first.Insert(new MemoryInventory().With(OneByThree, "exotic_fuel", 1), Actionable.Modulate, Player);
        second.Insert(new MemoryInventory().With(OneByThree, "exotic_fuel", 1).With(OneByThree, "exotic_fuel", 1),
            Actionable.Modulate, Player);
        var network = new NetworkStorage();
        network.Mount(0, first);
        network.Mount(1, second);
        var limited = new PieceLimitSink(new MemoryInventory(), 2);

        network.Extract(Fuel, long.MaxValue, limited, Actionable.Modulate, Player);

        Assert.That(limited.Accepted, Is.EqualTo(2));
        Assert.That(Pieces(network).Get(Fuel), Is.EqualTo(1));
    }

    [Test]
    public void PieceLimitSink_RefusesAPositiveLimitOnly()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>((System.Action)(() => _ = new PieceLimitSink(new MemoryInventory(), 0)));
    }

    [Test]
    public void FallbackRootSink_FillsThePreferredDestinationFirstThenTheNext()
    {
        var cell = NewCell();
        var source = new MemoryInventory();
        for (var i = 0; i < 5; i++) source.With(OneByThree, "exotic_fuel", 10 + i);
        cell.Insert(source, Actionable.Modulate, Player);
        var warehouse = new MemoryInventory { Capacity = 2 };
        var counter = new MemoryInventory();
        var limited = new PieceLimitSink(new FallbackRootSink(new IRootSink[] { warehouse, counter }), 4);

        cell.Extract(Fuel, long.MaxValue, limited, Actionable.Modulate, Player);

        Assert.That(limited.Accepted, Is.EqualTo(4));
        Assert.That(warehouse.Roots, Has.Count.EqualTo(2));
        Assert.That(counter.Roots, Has.Count.EqualTo(2));
        Assert.That(Pieces(cell).Get(Fuel), Is.EqualTo(1));
    }

    [Test]
    public void FallbackRootSink_StopsWhenEveryDestinationIsFull()
    {
        var cell = NewCell();
        cell.Insert(new MemoryInventory().With(OneByThree, "exotic_fuel", 1).With(OneByThree, "exotic_fuel", 2),
            Actionable.Modulate, Player);
        var first = new MemoryInventory { Capacity = 0 };
        var second = new MemoryInventory { Capacity = 1 };
        var limited = new PieceLimitSink(new FallbackRootSink(new IRootSink[] { first, second }), 5);

        var result = cell.Extract(Fuel, long.MaxValue, limited, Actionable.Modulate, Player);

        Assert.That(limited.Accepted, Is.EqualTo(1));
        Assert.That(second.Roots, Has.Count.EqualTo(1));
        Assert.That(result.StoppedBy.Rejection, Is.EqualTo(StoreRejection.PhysicalLimit));
        Assert.That(Pieces(cell).Get(Fuel), Is.EqualTo(1));
    }

    [Test]
    public void FallbackRootSink_DoesNotOfferAReturnedRootAgain()
    {
        var returning = new FixedSink(TransferStep.Returned);
        var next = new FixedSink(TransferStep.Moved);
        var sink = new FallbackRootSink(new IRootSink[] { returning, next });

        var step = sink.Accept(default, Actionable.Modulate, Player);

        Assert.That(step.Outcome, Is.EqualTo(TransferOutcome.Returned));
        Assert.That(next.Calls, Is.Zero);
    }

    [Test]
    public void FallbackRootSink_RequiresDestinations()
    {
        Assert.Throws<System.ArgumentException>((System.Action)(() => _ = new FallbackRootSink(new IRootSink[0])));
        Assert.Throws<System.ArgumentException>((System.Action)(() => _ = new FallbackRootSink(new IRootSink[] { null! })));
    }

    private sealed class FixedSink : IRootSink
    {
        private readonly TransferStep _step;

        internal FixedSink(TransferStep step) => _step = step;

        internal int Calls { get; private set; }

        public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
        {
            Calls++;
            return _step;
        }
    }

    private static BasicCellInventory NewCell()
    {
        var pages = new MemoryCellPages();
        return new BasicCellInventory(MemoryCellPages.EmptyView(StorageCellTier.All[0]), pages);
    }

    private static KeyCounter Pieces(IRootStorage storage)
    {
        var counter = new KeyCounter();
        storage.GetAvailablePieces(counter);
        return counter;
    }

    private static KeyCounter Units(IRootStorage storage)
    {
        var counter = new KeyCounter();
        storage.GetAvailableStacks(counter);
        return counter;
    }
}
