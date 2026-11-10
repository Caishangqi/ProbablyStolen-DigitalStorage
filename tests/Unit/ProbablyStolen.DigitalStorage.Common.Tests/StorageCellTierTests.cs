using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class StorageCellTierTests
{
    [Test]
    public void Catalog_ContainsExactUniqueDefinitions()
    {
        var tiers = StorageCellTier.All;
        Assert.That(tiers.Select(t => t.Name), Is.EqualTo(new[] { "1k", "4k", "16k", "64k", "256k" }));
        Assert.That(tiers.Select(t => t.Multiplier), Is.EqualTo(new[] { 1, 4, 16, 64, 256 }));
        Assert.That(tiers.Select(t => t.ComponentValue), Is.EqualTo(new[] { 250, 750, 2250, 6750, 20250 }));
        Assert.That(tiers.Select(t => t.MerchantWeight), Is.EqualTo(new[] { 50, 25, 15, 8, 2 }));
        Assert.That(tiers.Select(t => t.ComponentId).Distinct().Count(), Is.EqualTo(5));
        Assert.That(tiers.Select(t => t.CellId).Distinct().Count(), Is.EqualTo(5));
        foreach (var tier in tiers)
        {
            Assert.That(tier.ComponentId, Is.EqualTo("digital_storage.cell_component_" + tier.Name));
            Assert.That(tier.CellId, Is.EqualTo("digital_storage.item_storage_cell_" + tier.Name));
            Assert.That(StorageCellTier.FromComponentId(tier.ComponentId), Is.SameAs(tier));
            Assert.That(StorageCellTier.FromCellId(tier.CellId), Is.SameAs(tier));
        }
        Assert.That(StorageCellTier.FromCellId("digital_storage.item_storage_cell_128k"), Is.Null);
        Assert.That(StorageCellTier.FromComponentId("digital_storage.cell_component_1K"), Is.Null);
        Assert.That(StorageCellTier.FromCellId(null), Is.Null);
    }

    [TestCase(1, "1k")]
    [TestCase(50, "1k")]
    [TestCase(51, "4k")]
    [TestCase(75, "4k")]
    [TestCase(76, "16k")]
    [TestCase(90, "16k")]
    [TestCase(91, "64k")]
    [TestCase(98, "64k")]
    [TestCase(99, "256k")]
    [TestCase(100, "256k")]
    public void MerchantRoll_UsesInclusiveWeightedIntervals(int roll, string name)
    {
        Assert.That(StorageCellTier.FromMerchantRoll(roll).Name, Is.EqualTo(name));
    }

    [TestCase(0)]
    [TestCase(101)]
    public void MerchantRoll_RejectsOutOfRange(int roll)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>((System.Action)(() => StorageCellTier.FromMerchantRoll(roll)));
    }

    [Test]
    public void Pairing_OnlyMatchingTiersAreComplete()
    {
        var recipe = new StorageCellDisassemblyRecipe(new StorageComponent());
        foreach (var cell in StorageCellTier.All)
        {
            foreach (var component in StorageCellTier.All)
            {
                Assert.That(recipe.CanAssemble(StorageCellDisassemblyRecipe.HousingId, 1,
                    component.ComponentId, 1, 1, 2, false, false), Is.True);
                Assert.That(recipe.IsComplete(cell.CellId, 1, component.ComponentId, 1), Is.EqualTo(cell == component));
                Assert.That(recipe.IsComplete(cell.CellId, 2, component.ComponentId, 1), Is.False);
                Assert.That(recipe.IsComplete(cell.CellId, 1, component.ComponentId, 2), Is.False);
            }
        }
    }

    [TestCase(-1, 1)]
    [TestCase(1, 1)]
    [TestCase(16, 16)]
    [TestCase(128, 128)]
    [TestCase(int.MaxValue, 128)]
    public void Layout_ClampsBaseBeforeMultiplying(int input, int expectedBase)
    {
        foreach (var tier in StorageCellTier.All)
        {
            var layout = tier.Layout(input);
            Assert.That(layout.MaxPages, Is.EqualTo(expectedBase * tier.Multiplier));
            Assert.That(layout.MaxTypes, Is.EqualTo(63));
            Assert.That((layout.PageWidth, layout.PageHeight), Is.EqualTo((12, 12)));
        }
        Assert.That(StorageCellTier.All[4].Layout(128).MaxPages, Is.EqualTo(CellLayout.AbsoluteMaxPages));
    }
}
