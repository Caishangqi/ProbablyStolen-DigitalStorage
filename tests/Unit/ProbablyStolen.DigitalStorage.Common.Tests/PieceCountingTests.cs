using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
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

    private static BasicCellInventory NewCell()
    {
        var pages = new MemoryCellPages();
        return new BasicCellInventory(MemoryCellPages.EmptyView(StorageCellTier.All[0], 128, null, null), pages);
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
