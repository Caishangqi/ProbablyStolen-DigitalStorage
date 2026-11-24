using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests
{
    [TestFixture]
    internal sealed class StoredItemPresenceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void LiquidUpdate_StoredBottle_ResumesAfterLeavingCell(bool nested)
        {
            var cell = CellInventoryFixture.Create();
            Assert.That(CellPages.TryAppendStripPage(cell, PageGeometry.Strip(new BoundingSize(1, 2), 16)), Is.True);
            var page = CellInventoryFixture.Tabs(cell).GetTabInventory(1)!;
            var bottle = new GameItem("bottled_water");
            if (nested)
            {
                var box = new GameItem("box");
                page.Place(box);
                new GameGridInventory { Owner = box }.Place(bottle);
            }
            else
            {
                page.Place(bottle);
            }

            Assert.That(StoredItemPresence.ShouldUpdateLiquid(bottle), Is.False);

            var warehouse = new GameGridInventory();
            warehouse.Place(bottle);

            Assert.That(StoredItemPresence.ShouldUpdateLiquid(bottle), Is.True);
        }

        [Test]
        public void LiquidUpdate_LockedNonStorageGrid_RemainsEnabled()
        {
            var cell = NativeCellResolverTests.CompleteCell();
            var accessoryGrid = new GameGridInventory { Owner = cell, overrideLockInsert = true };
            var bottle = new GameItem("bottled_water");
            accessoryGrid.Place(bottle);

            Assert.That(StoredItemPresence.ShouldUpdateLiquid(bottle), Is.True);
        }
    }
}
