using System.Collections.Generic;
using NUnit.Framework;
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
            var cell = NativeCellResolverTests.CompleteCell();
            var page = new GameGridInventory { Owner = cell, overrideLockInsert = true };
            cell.StoragePages.Add(page);
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

namespace ProbablyStolen.DigitalStorage.Platform.Storage
{
    internal static class CellPages
    {
        internal static IReadOnlyList<GameGridInventory> Pages(GameItem cell) => cell.StoragePages;
    }
}
