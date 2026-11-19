using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellRecoveryPlannerTests
{
    private static readonly BoundingSize TwoByTwo = new(2, 2);
    private static readonly BoundingSize ThreeByThree = new(3, 3);
    private static readonly ItemKey Apple = new("item.apple");

    [Test]
    public void InvalidSlot_UsesFirstFreeSlotOnTheSameSizePage()
    {
        var pages = new[] { Anchor(), Strip(TwoByTwo, 0, 2), Strip(TwoByTwo, 0) };
        var plan = CellRecoveryPlanner.Plan(pages, new[] { new RecoveryRoot(1, -1, TwoByTwo, Apple, 12) });

        Assert.That(plan.IsSafe, Is.True);
        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        Assert.That((plan.Moves[0].TargetPage, plan.Moves[0].TargetSlot), Is.EqualTo((1, 1)));
        Assert.That(plan.Moves[0].AppendPage, Is.False);
    }

    [Test]
    public void MismatchedSize_UsesTheOtherPageForThatSize()
    {
        var pages = new[] { Anchor(), Strip(TwoByTwo, 0), Strip(ThreeByThree, 0, 1) };
        var plan = CellRecoveryPlanner.Plan(pages, new[] { new RecoveryRoot(1, 0, ThreeByThree, Apple, 1) });

        Assert.That(plan.IsSafe, Is.True);
        Assert.That((plan.Moves[0].TargetPage, plan.Moves[0].TargetSlot), Is.EqualTo((2, 2)));
    }

    [Test]
    public void NoExistingChain_AppendsExactlyOneMatchingPage()
    {
        var pages = new[] { Anchor(), Strip(TwoByTwo, Enumerable.Range(0, 16).ToArray()) };
        var plan = CellRecoveryPlanner.Plan(pages,
            new[] { new RecoveryRoot(1, -1, ThreeByThree, Apple, 1) });

        Assert.That(plan.IsSafe, Is.True);
        Assert.That(plan.Moves[0].AppendPage, Is.True);
        Assert.That((plan.Moves[0].TargetPage, plan.Moves[0].TargetSlot), Is.EqualTo((2, 0)));
    }

    [Test]
    public void MultipleRoots_ReuseOneNewPageBeforeAppendingAnother()
    {
        var pages = new[] { Anchor(), Strip(TwoByTwo, Enumerable.Range(0, 16).ToArray()) };
        var roots = new[]
        {
            new RecoveryRoot(1, -1, ThreeByThree, Apple, 1),
            new RecoveryRoot(1, -1, ThreeByThree, Apple, 2)
        };
        var plan = CellRecoveryPlanner.Plan(pages, roots);

        Assert.That(plan.IsSafe, Is.True);
        Assert.That(plan.Moves, Has.Count.EqualTo(2));
        Assert.That(plan.Moves[0].AppendPage, Is.True);
        Assert.That((plan.Moves[1].TargetPage, plan.Moves[1].TargetSlot), Is.EqualTo((2, 1)));
        Assert.That(plan.Moves[1].AppendPage, Is.False);
    }
    [Test]
    public void UnsupportedSize_LeavesPlanEmptyAndUnavailable()
    {
        var plan = CellRecoveryPlanner.Plan(new[] { Anchor() },
            new[] { new RecoveryRoot(0, -1, new BoundingSize(13, 1), Apple, 1) });

        Assert.That(plan.Failure, Is.EqualTo(RecoveryFailure.UnsupportedSize));
        Assert.That(plan.Moves, Is.Empty);
    }

    [Test]
    public void ConflictingDuplicateClaims_RefuseInconsistentPlannerInput()
    {
        var roots = new[]
        {
            new RecoveryRoot(1, 0, TwoByTwo, Apple, 1),
            new RecoveryRoot(1, 0, TwoByTwo, Apple, 1)
        };
        var plan = CellRecoveryPlanner.Plan(new[] { Anchor(), Strip(TwoByTwo, 0) }, roots);

        Assert.That(plan.Failure, Is.EqualTo(RecoveryFailure.DuplicateSlot));
    }

    [Test]
    public void FullTargetAndPageLimit_RefuseWithoutAPlan()
    {
        var full = Enumerable.Range(0, 16).ToArray();
        var pages = Enumerable.Range(0, LayoutDescriptor.MaxPages)
            .Select(index => index == 0 ? Anchor() : Strip(TwoByTwo, full)).ToArray();
        var plan = CellRecoveryPlanner.Plan(pages,
            new[] { new RecoveryRoot(1, 0, ThreeByThree, Apple, 1) });

        Assert.That(plan.Failure, Is.EqualTo(RecoveryFailure.PageLimit));
        Assert.That(plan.Moves, Is.Empty);
    }

    [Test]
    public void ProtectedPage_IsExcludedAsSourceAndDestination()
    {
        var pages = new[]
        {
            Anchor(),
            PageView.Protected(StripLayout.PageFor(TwoByTwo), 2),
            Strip(TwoByTwo, 1)
        };
        var roots = new[]
        {
            new RecoveryRoot(1, 0, ThreeByThree, Apple, 1),
            new RecoveryRoot(2, -1, TwoByTwo, Apple, 2)
        };

        var plan = CellRecoveryPlanner.Plan(pages, roots);

        Assert.That(plan.IsSafe, Is.True);
        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        Assert.That(plan.Moves[0].SourcePage, Is.EqualTo(2));
        Assert.That(plan.Moves[0].TargetPage, Is.EqualTo(2));
        Assert.That(plan.Moves[0].TargetSlot, Is.EqualTo(0));
    }

    [Test]
    public void ProtectedPage_DoesNotBlockHealthySourceRelocation()
    {
        var pages = new[]
        {
            Anchor(),
            PageView.Protected(StripLayout.PageFor(TwoByTwo), 1),
            Strip(ThreeByThree, 0)
        };
        var plan = CellRecoveryPlanner.Plan(pages,
            new[] { new RecoveryRoot(2, 0, TwoByTwo, Apple, 1) });

        Assert.That(plan.IsSafe, Is.True);
        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        Assert.That(plan.Moves[0].AppendPage, Is.True);
        Assert.That(plan.Moves[0].TargetPage, Is.EqualTo(3));
    }

    [Test]
    public void DuplicateSlotPage_IsProtectedWhileHealthyPagesRemainPlannable()
    {
        var pages = new[]
        {
            Anchor(),
            PageView.Protected(StripLayout.PageFor(TwoByTwo), 2),
            Strip(ThreeByThree, 0)
        };
        var plan = CellRecoveryPlanner.Plan(pages,
            new[] { new RecoveryRoot(2, 0, TwoByTwo, Apple, 1) });

        Assert.That(plan.IsSafe, Is.True);
        Assert.That(plan.Moves, Has.Count.EqualTo(1));
        Assert.That(plan.Moves[0].SourcePage, Is.EqualTo(2));
        Assert.That(plan.Moves[0].AppendPage, Is.True);
    }

    [Test]
    public void AlreadyLegalLayout_IsIdempotent()
    {
        var pages = new[] { Anchor(), Strip(TwoByTwo, 0, 1) };
        var roots = new[]
        {
            new RecoveryRoot(1, 0, TwoByTwo, Apple, 1),
            new RecoveryRoot(1, 1, TwoByTwo, Apple, 2)
        };

        var plan = CellRecoveryPlanner.Plan(pages, roots);

        Assert.That(plan.IsSafe, Is.True);
        Assert.That(plan.Moves, Is.Empty);
    }

    private static PageView Anchor() => PageView.Unslotted(PageGeometry.Anchor, 0);

    private static PageView Strip(BoundingSize size, params int[] slots) =>
        PageView.Strip(StripLayout.PageFor(size), slots);
}
