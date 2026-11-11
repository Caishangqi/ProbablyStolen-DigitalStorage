using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.NativeContent;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

/// <summary>
/// Exercises the linked production bay resolver against synthetic doubles. Native inventory, shape, and
/// save behavior are not simulated, so this proves only the resolver's bay and assembly filtering.
/// </summary>
[TestFixture]
public sealed class NativeCellResolverTests
{
    [Test]
    public void Resolver_MountedComponent_ReportsOneAvailableCell()
    {
        var drive = new GameItem("digital_storage.storage_drive");
        var bay = Bay();
        var mountedCell = CompleteCell();
        bay.childItem = mountedCell;
        drive.Slots.Add(bay);

        var cells = NativeCellResolver.Get(drive);

        Assert.That(cells, Has.Count.EqualTo(1));
        var cell = cells[0];
        Assert.That(cell.GetStatus(), Is.EqualTo(CellState.Empty));

        Assert.That(bay.childItem, Is.SameAs(mountedCell));
        Assert.That(mountedCell.Slots.Single().childItem, Is.Not.Null);
    }

    [Test]
    public void Resolver_TwoMountedCells_GetDistinctStableInstances()
    {
        // Regression: a shared instance made GridStorage reject every network with two or more cells.
        var drive = new GameItem("digital_storage.storage_drive");
        var first = Bay();
        first.childItem = CompleteCell();
        var second = Bay();
        second.childItem = CompleteCell();
        drive.Slots.Add(first);
        drive.Slots.Add(second);

        var cells = NativeCellResolver.Get(drive);
        var again = NativeCellResolver.Get(drive);

        Assert.That(cells, Has.Count.EqualTo(2));
        Assert.That(cells[0], Is.Not.SameAs(cells[1]));
        Assert.That(again[0], Is.SameAs(cells[0]));
        Assert.That(again[1], Is.SameAs(cells[1]));
    }

    [Test]
    public void Resolver_NonBaySlotIncompleteCellOrMissingComponent_ReportsNoCell()
    {
        var drive = new GameItem("digital_storage.storage_drive");
        drive.Slots.Add(new GameSlotInventory { identifier = "module", childItem = new GameItem("module") });

        var unnumbered = new GameSlotInventory { childItem = CompleteCell() };
        drive.Slots.Add(unnumbered);

        var wrongIdentifier = Bay();
        wrongIdentifier.childItem = new GameItem(StorageCellDisassemblyRecipe.HousingId);
        drive.Slots.Add(wrongIdentifier);

        drive.Slots.Add(Bay());

        var componentless = Bay();
        componentless.childItem = new GameItem(StorageCellDisassemblyRecipe.CellId);
        drive.Slots.Add(componentless);

        Assert.That(NativeCellResolver.Get(drive), Is.Empty);
    }

    [Test]
    public void Resolver_AllTiers_RequiresMatchingComponentAndSingleCounts()
    {
        foreach (var tier in StorageCellTier.All)
        {
            var cell = CompleteCell();
            cell.identifier = tier.CellId;
            var component = cell.Slots.Single().childItem!;
            component.identifier = tier.ComponentId;
            Assert.That(BasicCellHandler.IsCell(cell), Is.True, tier.Name);
            foreach (var other in StorageCellTier.All.Where(other => other != tier))
            {
                component.identifier = other.ComponentId;
                Assert.That(BasicCellHandler.IsCell(cell), Is.False, tier.Name + "/" + other.Name);
            }
            component.identifier = tier.ComponentId;
            component.unitCount = 2;
            Assert.That(BasicCellHandler.IsCell(cell), Is.False);
            component.unitCount = 1;
            cell.unitCount = 2;
            Assert.That(BasicCellHandler.IsCell(cell), Is.False);
        }
    }

    internal static GameSlotInventory Bay() => new() { identifier = CellContentWindow.BayPrefix + "0" };

    internal static GameItem CompleteCell()
    {
        var cell = new GameItem(StorageCellDisassemblyRecipe.CellId);
        cell.Slots.Add(new GameSlotInventory
        {
            identifier = CellContentWindow.SlotId,
            childItem = new GameItem("digital_storage.cell_component_1k")
        });
        return cell;
    }
}
