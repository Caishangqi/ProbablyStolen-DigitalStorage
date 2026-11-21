using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
internal sealed class TerminalDropPreviewTests
{
    [Test]
    public void GroupPreview_DefersCapacityWithoutCallingStoreRefusals()
    {
        var first = OpaqueItem();
        var second = OpaqueItem();
        GameItem[] items = { first, second };
        var calls = 0;

        var preview = TerminalDropPreview.Evaluate(items, groupDrag: true, amount: 0,
            storeRefusals: (_, _) =>
            {
                calls++;
                return Array.Empty<string?>();
            }, unknownRefusal: "unknown");

        Assert.That(preview.State, Is.EqualTo(TerminalDropPreviewState.Pending));
        Assert.That(preview.RefusedItem, Is.Null);
        Assert.That(preview.Refusal, Is.Null);
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public void SinglePreview_StillChecksAndReportsTheFirstRefusal()
    {
        var item = OpaqueItem();
        var calls = 0;

        var preview = TerminalDropPreview.Evaluate(new[] { item }, groupDrag: false, amount: 1,
            storeRefusals: (_, amount) =>
            {
                calls++;
                Assert.That(amount, Is.EqualTo(1));
                return new string?[] { "rejected" };
            }, unknownRefusal: "unknown");

        Assert.That(preview.State, Is.EqualTo(TerminalDropPreviewState.Rejected));
        Assert.That(preview.RefusedItem, Is.SameAs(item));
        Assert.That(preview.Refusal, Is.EqualTo("rejected"));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void GroupIconTarget_UsesStaticAdmissionWithoutCapacitySimulation()
    {
        var staticChecks = 0;
        var capacityChecks = 0;

        var accepted = TerminalItemDrop.CanTargetStorage(groupDrag: true, groupRelease: false,
            staticAdmission: () =>
            {
                staticChecks++;
                return true;
            },
            capacityAdmission: () =>
            {
                capacityChecks++;
                return false;
            });

        Assert.That(accepted, Is.True);
        Assert.That(staticChecks, Is.EqualTo(1));
        Assert.That(capacityChecks, Is.Zero);
    }

    [Test]
    public void GroupRelease_CanTargetRetainsCapacityPreflight()
    {
        var staticChecks = 0;
        var capacityChecks = 0;

        var accepted = TerminalItemDrop.CanTargetStorage(groupDrag: true, groupRelease: true,
            staticAdmission: () =>
            {
                staticChecks++;
                return true;
            },
            capacityAdmission: () =>
            {
                capacityChecks++;
                return false;
            });

        Assert.That(accepted, Is.False);
        Assert.That(staticChecks, Is.Zero);
        Assert.That(capacityChecks, Is.EqualTo(1));
    }

    [Test]
    public void SingleIconTarget_RetainsCapacitySimulation()
    {
        var staticChecks = 0;
        var capacityChecks = 0;

        var accepted = TerminalItemDrop.CanTargetStorage(groupDrag: false, groupRelease: false,
            staticAdmission: () =>
            {
                staticChecks++;
                return false;
            },
            capacityAdmission: () =>
            {
                capacityChecks++;
                return true;
            });

        Assert.That(accepted, Is.True);
        Assert.That(staticChecks, Is.Zero);
        Assert.That(capacityChecks, Is.EqualTo(1));
    }

    [Test]
    public void ThrottlePreview_RequiresExactCachedSingle()
    {
        var first = OpaqueItem();
        var second = OpaqueItem();
        var third = OpaqueItem();
        GameItem[] cachedItems = { first, second };
        var cachedCounts = new[] { 5, 2 };
        var accepted = TerminalDropPreview.Evaluate(cachedItems, groupDrag: false, amount: 1,
            storeRefusals: (_, _) => new string?[] { null, null }, unknownRefusal: "unknown");

        var exact = TerminalDropPreview.ForThrottledObservation(groupDrag: false, preview: accepted,
            previewGroup: false, previewAmount: 1, previewItems: cachedItems, previewCounts: cachedCounts,
            items: cachedItems, itemCounts: cachedCounts, amount: 1);
        var changedIdentity = TerminalDropPreview.ForThrottledObservation(groupDrag: false, preview: accepted,
            previewGroup: false, previewAmount: 1, previewItems: cachedItems, previewCounts: cachedCounts,
            items: new GameItem[] { first, third }, itemCounts: cachedCounts, amount: 1);
        var changedCount = TerminalDropPreview.ForThrottledObservation(groupDrag: false, preview: accepted,
            previewGroup: false, previewAmount: 1, previewItems: cachedItems, previewCounts: cachedCounts,
            items: cachedItems, itemCounts: new[] { 5, 3 }, amount: 1);
        var changedAmount = TerminalDropPreview.ForThrottledObservation(groupDrag: false, preview: accepted,
            previewGroup: false, previewAmount: 1, previewItems: cachedItems, previewCounts: cachedCounts,
            items: cachedItems, itemCounts: cachedCounts, amount: 2);
        var singleAfterGroup = TerminalDropPreview.ForThrottledObservation(groupDrag: false,
            preview: TerminalDropPreview.Pending(), previewGroup: true, previewAmount: 1,
            previewItems: cachedItems, previewCounts: cachedCounts, items: cachedItems,
            itemCounts: cachedCounts, amount: 1);
        var groupAfterSingle = TerminalDropPreview.ForThrottledObservation(groupDrag: true, preview: accepted,
            previewGroup: false, previewAmount: 1, previewItems: cachedItems, previewCounts: cachedCounts,
            items: cachedItems, itemCounts: cachedCounts, amount: 1);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(exact.State, Is.EqualTo(TerminalDropPreviewState.Accepted));
            Assert.That(changedIdentity.State, Is.EqualTo(TerminalDropPreviewState.Unavailable));
            Assert.That(changedCount.State, Is.EqualTo(TerminalDropPreviewState.Unavailable));
            Assert.That(changedAmount.State, Is.EqualTo(TerminalDropPreviewState.Unavailable));
            Assert.That(singleAfterGroup.State, Is.EqualTo(TerminalDropPreviewState.Unavailable));
            Assert.That(groupAfterSingle.State, Is.EqualTo(TerminalDropPreviewState.Pending));
        }));
    }

    [Test]
    public void GroupReleaseScopes_AreOwnedAndIdempotent()
    {
        Assert.That(TerminalItemDropRouter.IsGroupDropInProgress, Is.False);
        var outer = new TerminalItemDropRouter.GroupDropScope();
        var inner = new TerminalItemDropRouter.GroupDropScope();

        Assert.That(TerminalItemDropRouter.IsGroupDropInProgress, Is.True);
        TerminalItemDropRouter.CompleteDrop(null);
        Assert.That(TerminalItemDropRouter.IsGroupDropInProgress, Is.True);
        TerminalItemDropRouter.CompleteDrop(inner);
        Assert.That(TerminalItemDropRouter.IsGroupDropInProgress, Is.True);
        TerminalItemDropRouter.CompleteDrop(inner);
        Assert.That(TerminalItemDropRouter.IsGroupDropInProgress, Is.True);
        outer.Dispose();

        Assert.That(TerminalItemDropRouter.IsGroupDropInProgress, Is.False);
        outer.Dispose();
        Assert.That(TerminalItemDropRouter.IsGroupDropInProgress, Is.False);
    }

    [Test]
    public void GroupHoverHighlight_PreservesNativeActionPriority()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(TerminalItemDropRouter.ShouldPaintGroupStoreFeedback(nativeAction: true, storageCandidate: true), Is.False);
            Assert.That(TerminalItemDropRouter.ShouldPaintGroupStoreFeedback(nativeAction: false, storageCandidate: true), Is.True);
            Assert.That(TerminalItemDropRouter.ShouldPaintGroupStoreFeedback(nativeAction: false, storageCandidate: false), Is.False);
        }));
    }

    private static GameItemElement OpaqueItem() =>
        (GameItemElement)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
}
