using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.NativeContent;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class IoPortCellBayTests
{
    [TestCase(0)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(11)]
    public void CellSnapshot_ReportsMountedInEitherBankUntilTheCellLeaves(int index)
    {
        var cell = new GameItem(StorageCellDisassemblyRecipe.CellId);
        var bay = Bay(IoPortItem.ItemId, "digital_storage.io_port_bay." + index, cell);
        bay.Place(cell);

        Assert.That(ContentSnapshots.Cell(cell).Mounted, Is.True);

        new GameGridInventory().Place(cell);
        Assert.That(ContentSnapshots.Cell(cell).Mounted, Is.False);
    }

    [TestCase(DriveItem.ItemId, "digital_storage.io_port_bay.0", true)]
    [TestCase(IoPortItem.ItemId, "unrelated_slot", true)]
    [TestCase(IoPortItem.ItemId, "digital_storage.io_port_bay.0", false)]
    public void CellSnapshot_RequiresTheIoPortOwnerBayAndCurrentOccupant(string owner, string slot, bool isOccupant)
    {
        var cell = new GameItem(StorageCellDisassemblyRecipe.CellId);
        var bay = Bay(owner, slot, isOccupant ? cell : new GameItem(StorageCellDisassemblyRecipe.CellId));
        bay.Place(cell);

        Assert.That(ContentSnapshots.Cell(cell).Mounted, Is.False);
    }

    private static GameSlotInventory Bay(string owner, string slot, GameItem occupant) => new()
    {
        Owner = new GameItem(owner),
        identifier = slot,
        childItem = occupant
    };
}
