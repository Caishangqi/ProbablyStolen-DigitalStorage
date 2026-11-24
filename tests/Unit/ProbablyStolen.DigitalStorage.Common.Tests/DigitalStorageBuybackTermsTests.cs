using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Commerce;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class DigitalStorageBuybackTermsTests
{
    [Test]
    public void Terms_UseApprovedPerVisitBudget()
    {
        Assert.That(DigitalStorageBuybackTerms.VisitBudget, Is.EqualTo(800));
    }

    [Test]
    public void CanAccept_RequiresWhitelistedIdAndPositiveValueWithinRemainingBudget()
    {
        var component = StorageCellTier.All[0].ComponentId;

        Assert.That(DigitalStorageBuybackTerms.CanAccept(component, 800, 800, 800), Is.True);
        Assert.That(DigitalStorageBuybackTerms.CanAccept(component, 800, 800, 1), Is.True);
        Assert.That(DigitalStorageBuybackTerms.CanAccept(component, 801, 800, 800), Is.False);
        Assert.That(DigitalStorageBuybackTerms.CanAccept(component, 201, 200, 800), Is.False);
        Assert.That(DigitalStorageBuybackTerms.CanAccept(component, 0, 800, 800), Is.False);
        Assert.That(DigitalStorageBuybackTerms.CanAccept("digital_storage.io_port", 1, 800, 800), Is.False);
        Assert.That(DigitalStorageBuybackTerms.CanAccept(component, 1, 0, 800), Is.False);
        Assert.That(DigitalStorageBuybackTerms.CanAccept(component, 1, 800, 0), Is.False);
    }

    [TestCase(800, 300, 500)]
    [TestCase(500, 800, 0)]
    [TestCase(300, -10, 300)]
    [TestCase(0, 10, 0)]
    [TestCase(-1, 10, 0)]
    public void RemainingAfterSale_ClampsPayoutToCurrentAllowance(int remaining, int payout, int expected)
    {
        Assert.That(DigitalStorageBuybackTerms.RemainingAfterSale(remaining, payout), Is.EqualTo(expected));
    }

    [Test]
    public void Accepts_OnlyStandaloneStorageComponents()
    {
        foreach (var tier in StorageCellTier.All)
            Assert.That(DigitalStorageBuybackTerms.Accepts(tier.ComponentId), Is.True, tier.ComponentId);

        Assert.That(DigitalStorageBuybackTerms.Accepts("digital_storage.item_cell_housing"), Is.False);
        Assert.That(DigitalStorageBuybackTerms.Accepts("digital_storage.item_storage_cell_64k"), Is.False);
        Assert.That(DigitalStorageBuybackTerms.Accepts("digital_storage.io_port"), Is.False);
        Assert.That(DigitalStorageBuybackTerms.Accepts("digital_storage.component_module"), Is.False);
        Assert.That(DigitalStorageBuybackTerms.Accepts(null), Is.False);
    }
}
