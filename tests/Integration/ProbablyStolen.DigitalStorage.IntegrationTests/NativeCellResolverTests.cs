using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.NativeContent;
using ProbablyStolen.DigitalStorage.Platform.Network;

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
