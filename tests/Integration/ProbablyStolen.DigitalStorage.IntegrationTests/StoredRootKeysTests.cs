using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
internal sealed class StoredRootKeysTests
{
    private GameItem _cell = null!;
    private GameGridInventory _page = null!;

    [SetUp]
    public void SetUp()
    {
        StoredRootKeys.Clear();
        TransferProfile.Reset();
        TransferProfile.Resume();
        _cell = CellInventoryFixture.Create();
        Assert.That(CellPages.TryAppendStripPage(_cell, PageGeometry.Strip(new BoundingSize(1, 2), 16)), Is.True);
        _page = CellPages.Pages(_cell)[1];
    }

    [TearDown]
    public void TearDown()
    {
        TransferProfile.Pause();
        TransferProfile.Reset();
        CellInventoryCache.Forget(_cell);
        StoredRootKeys.Clear();
    }

    [Test]
    public void Identify_KeepsTheTypeWhileTheRootStaysOnItsPage()
    {
        var root = Root("bread", 0);

        Assert.That(StoredRootKeys.Identify(root), Is.EqualTo(new ItemKey("bread")));
        Assert.That(StoredRootKeys.Identify(root), Is.EqualTo(new ItemKey("bread")));

        Assert.That(Kept(), Is.EqualTo(1));
    }

    [Test]
    public void Identify_OnAnotherPage_ReadsTheRootAgain()
    {
        var root = Root("bread", 0);
        StoredRootKeys.Identify(root);
        var other = new GameGridInventory();
        other.Place(root);

        StoredRootKeys.Identify(root);

        Assert.That(Kept(), Is.Zero);
    }

    [Test]
    public void Identify_AfterAnUnreportedChange_ReadsTheRootAgain()
    {
        var root = Root("bread", 0);
        StoredRootKeys.Identify(root);

        root.itemFeatures.Add(new ItemFeature("quality"));
        StoredRootKeys.Identify(root);
        root.shortDescription = "Stale";
        StoredRootKeys.Identify(root);
        root.identifier = "cheese";

        Assert.That(StoredRootKeys.Identify(root), Is.EqualTo(new ItemKey("cheese")));
        Assert.That(Kept(), Is.Zero);
    }

    [Test]
    public void LeavingThePage_DropsTheKeptType()
    {
        var root = Root("bread", 0);
        StoredRootKeys.Identify(root);

        _page.onSlotRemoveItemEndFunc!(root, _page, null);
        StoredRootKeys.Identify(root);

        Assert.That(Kept(), Is.Zero);
    }

    [Test]
    public void Remember_KeepsTheTypeOfAStoredRoot()
    {
        var root = Root("bread", 0);

        StoredRootKeys.Remember(root, new ItemKey("bread"));

        Assert.That(StoredRootKeys.Identify(root), Is.EqualTo(new ItemKey("bread")));
        Assert.That(Kept(), Is.EqualTo(1));
    }

    [Test]
    public void ReadCached_AfterTheCellIsForgotten_ReadsOnlyRootsWithoutAKeptType()
    {
        Root("bread", 0);
        Root("cheese", 1);
        CellPages.ReadCached(_cell, CellWriteState.Writable);
        Root("apple", 2);
        CellInventoryCache.Forget(_cell);

        var view = CellPages.ReadCached(_cell, CellWriteState.Writable);

        Assert.That(Kept(), Is.EqualTo(2));
        Assert.That(Enumerable.Range(0, 3).Select(slot => view.KeyAt(1, slot)),
            Is.EqualTo(new[] { new ItemKey("bread"), new ItemKey("cheese"), new ItemKey("apple") }));
    }

    [Test]
    public void ForgetWithRoots_ReadsEveryRootOfTheCellAgain()
    {
        Root("bread", 0);
        Root("cheese", 1);
        CellPages.ReadCached(_cell, CellWriteState.Writable);

        CellInventoryCache.ForgetWithRoots(_cell);
        CellPages.ReadCached(_cell, CellWriteState.Writable);

        Assert.That(Kept(), Is.Zero);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void NoticedChange_DropsTheKeptType(bool insideRead, bool restorable)
    {
        var root = Root("bread", 0);
        var checks = new StoredRootChecks<GameItem, GameItem>(_ => { }, (_, _) => true, _ => { }, () => { });
        StoredItemChanges.Activate(new StoredItemChanges(item => ReferenceEquals(item, root) ? (_cell, root) : null,
            () => false, checks));
        try
        {
            StoredRootKeys.Identify(root);
            if (insideRead)
            {
                using (StoredItemChanges.Read(root)) StoredItemChanges.Changed(root, restorable);
            }
            else
            {
                StoredItemChanges.Changed(root, restorable);
            }

            StoredRootKeys.Identify(root);
            Assert.That(Kept(), Is.Zero);
        }
        finally
        {
            StoredItemChanges.Activate(null);
        }
    }

    [Test]
    public void RestoredChangeInsideARead_KeepsTheType()
    {
        var root = Root("bread", 0);
        var checks = new StoredRootChecks<GameItem, GameItem>(_ => { }, (_, _) => true, _ => { }, () => { });
        StoredItemChanges.Activate(new StoredItemChanges(item => ReferenceEquals(item, root) ? (_cell, root) : null,
            () => false, checks));
        try
        {
            StoredRootKeys.Identify(root);
            using (StoredItemChanges.Read(root))
            {
                StoredItemChanges.Changing(root);
                root.shortDescription = "Wrapped";
                StoredItemChanges.Changed(root, restorable: true);
            }

            StoredRootKeys.Identify(root);
            Assert.That(Kept(), Is.EqualTo(1));
        }
        finally
        {
            StoredItemChanges.Activate(null);
        }
    }

    [Test]
    public void Clear_DropsEveryKeptType()
    {
        var root = Root("bread", 0);
        StoredRootKeys.Identify(root);

        StoredRootKeys.Clear();
        StoredRootKeys.Identify(root);

        Assert.That(Kept(), Is.Zero);
    }

    private static int Kept() => TransferProfile.CallsOf(TransferPart.IdentifyKept);

    private GameItem Root(string id, int slot)
    {
        var root = new GameItem(id) { modifiedShape = new GridShape(slot, 0) };
        _page.Place(root);
        return root;
    }
}
