using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Commerce;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class DigitalStorageOffersTests
{
    [TestCase(0, false)]
    [TestCase(5, true)]
    [TestCase(12, true)]
    [TestCase(19, true)]
    [TestCase(4, false)]
    [TestCase(6, false)]
    [TestCase(7, false)]
    [TestCase(-2, false)]
    public void ScientistVisits_FollowsTheRetiredFarmerDay(int day, bool expected)
    {
        Assert.That(DigitalStorageOffers.ScientistVisits(day), Is.EqualTo(expected));
    }

    [Test]
    public void ScientistStock_BeforeAdvancedUpgrade_IsTheBasicSet()
    {
        foreach (var day in new[] { 5, 12, 19, 26 })
        {
            Assert.That(Names(DigitalStorageOffers.ScientistStock(day, advanced: false)), Is.EqualTo(new[]
            {
                "Housing", "Housing", "Component 1k", "Component 1k", "Component 4k", "TypeLimit Common"
            }));
        }
    }

    [TestCase(5, new[] { "Component 64k", "TypeLimit Rare" })]
    [TestCase(12, new[] { "Component 256k", "EqualDistributionModule", "StackDisplayModule" })]
    [TestCase(19, new[] { "Component 16k", "Component 16k", "TypeLimit Epic" })]
    [TestCase(26, new[] { "Component 64k", "TypeLimit Rare" })]
    public void ScientistStock_AfterAdvancedUpgrade_AddsTheWeeklyRotation(int day, string[] rotation)
    {
        var basic = Names(DigitalStorageOffers.ScientistStock(day, advanced: false));
        Assert.That(Names(DigitalStorageOffers.ScientistStock(day, advanced: true)), Is.EqualTo(basic.Concat(rotation)));
    }

    [TestCase(5, "EqualDistributionModule", "Component 16k")]
    [TestCase(12, "StackDisplayModule", "Component 64k")]
    [TestCase(19, "TypeLimit Common", "Component 16k")]
    [TestCase(26, "EqualDistributionModule", "Component 64k")]
    public void JacksonGuaranteedStock_RotatesOneModuleAndOneHighComponent(int day, string module, string component)
    {
        Assert.That(Names(DigitalStorageOffers.JacksonGuaranteedStock(day)), Is.EqualTo(new[] { module, component }));
    }

    private static string[] Names(System.Collections.Generic.IEnumerable<StorageOffer> offers) =>
        offers.Select(offer => offer.ToString()).ToArray();
}
