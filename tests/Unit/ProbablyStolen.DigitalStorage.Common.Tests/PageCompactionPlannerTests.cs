using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class PageCompactionPlannerTests
{
    private static LayoutDescriptor Parse(string text)
    {
        Assert.That(LayoutDescriptor.TryParse(text, out var descriptor), Is.True);
        return descriptor!;
    }

    [Test]
    public void MiddlePage_IsRemovedAndOrderIsKept()
    {
        var plan = PageCompactionPlanner.Plan(Parse("A;S1x1x16;S2x2x16;S3x3x16"), new[] { 0, 4, 0, 1 });

        Assert.That(plan.Kept, Is.EqualTo(new[] { 0, 1, 3 }));
        Assert.That(plan.Removed, Is.EqualTo(new[] { 2 }));
        Assert.That(plan.After.Format(), Is.EqualTo("A;S1x1x16;S3x3x16"));
    }

    [Test]
    public void LastPage_IsRemoved()
    {
        var plan = PageCompactionPlanner.Plan(Parse("A;S1x1x16*2"), new[] { 0, 3, 0 });

        Assert.That(plan.After.Format(), Is.EqualTo("A;S1x1x16"));
    }

    [Test]
    public void AllEmpty_ShrinksToTheAnchor()
    {
        var plan = PageCompactionPlanner.Plan(Parse("A;L*2;S1x1x16"), new[] { 0, 0, 0, 0 });

        Assert.That(plan.Kept, Is.EqualTo(new[] { 0 }));
        Assert.That(plan.After.Format(), Is.EqualTo("A"));
    }

    [Test]
    public void AnchorStaysEvenWhenItHoldsRootsOrIsEmpty()
    {
        var noWork = PageCompactionPlanner.Plan(Parse("A;S1x1x16"), new[] { 2, 1 });

        Assert.That(noWork.HasWork, Is.False);
        Assert.That(noWork.Kept, Is.EqualTo(new[] { 0, 1 }));
        Assert.That(PageCompactionPlanner.Plan(Parse("A"), new[] { 0 }).HasWork, Is.False);
    }

    [Test]
    public void MismatchedCounts_AreRejected()
    {
        Assert.Throws<ArgumentException>(new Action(() => PageCompactionPlanner.Plan(Parse("A;L"), new[] { 0 })));
        Assert.Throws<ArgumentException>(new Action(() => PageCompactionPlanner.Plan(Parse("A;L"), new[] { 0, -1 })));
    }
}
