using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class StoreAdmissionTests
{
    [Test]
    public void Refusal_AdmitsAnOwnedPlainItem()
    {
        Assert.That(StoreAdmission.Refusal(new GameItem("APPLE")), Is.Null);
    }

    [Test]
    public void Refusal_RejectsCounterGoodsWithoutTouchingThem()
    {
        var counter = new GameGridInventory();
        var item = new GameItem("APPLE") { Owned = false, unitCount = 3 };
        counter.Place(item);

        Assert.That(StoreAdmission.Refusal(item), Is.EqualTo(StoreAdmission.NotOwnedKey));
        Assert.That(item.parentInventory, Is.SameAs(counter));
        Assert.That(counter.childItems, Is.EquivalentTo(new[] { item }));
        Assert.That(item.unitCount, Is.EqualTo(3));
        Assert.That(item.Owned, Is.False);
    }

    [Test]
    public void Refusal_JudgesOwnershipOnTheRootLikeThePlayerInventory()
    {
        var bag = new GameItem("BAG");
        bag.Children.Add(new GameItem("APPLE") { Owned = false });
        var unownedBag = new GameItem("BAG") { Owned = false };
        unownedBag.Children.Add(new GameItem("APPLE"));

        Assert.That(StoreAdmission.Refusal(bag), Is.Null);
        Assert.That(StoreAdmission.Refusal(unownedBag), Is.EqualTo(StoreAdmission.NotOwnedKey));
    }

    [Test]
    public void Refusal_RejectsModItemsAndTheirContainersBeforeOwnership()
    {
        var cell = new GameItem(StoreAdmission.ModItemPrefix + "cell") { Owned = false };
        var bag = new GameItem("BAG");
        bag.Children.Add(new GameItem(StoreAdmission.ModItemPrefix + "cell"));

        Assert.That(StoreAdmission.Refusal(cell), Is.EqualTo(StoreAdmission.UnsupportedKey));
        Assert.That(StoreAdmission.Refusal(bag), Is.EqualTo(StoreAdmission.UnsupportedKey));
    }
}
