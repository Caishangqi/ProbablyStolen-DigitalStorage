using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Workbench;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Workbench;

[TestFixture]
public sealed class PartitionViewRefreshCoordinatorTests
{
    [Test]
    public void DetailStructuralRefresh_RebuildsTheVisibleDetailAndKeepsTheDraftOwner()
    {
        var draft = new object();
        var activeDraft = draft;
        var overviewRefreshes = 0;
        var detailRefreshes = 0;

        foreach (var change in new[] { "sample-expand", "sample-collapse", "condition-edit" })
        {
            PartitionViewRefreshCoordinator.Refresh(hasDetailDraft: true, detailScreenVisible: true,
                () => overviewRefreshes++, () =>
                {
                    detailRefreshes++;
                    Assert.That(activeDraft, Is.SameAs(draft), "detail structural change");
                });
        }

        Assert.That(detailRefreshes, Is.EqualTo(3));
        Assert.That(overviewRefreshes, Is.Zero);
    }

    [TestCase(80f, 440f, 172f, 80f)]
    [TestCase(80f, 220f, 172f, 48f)]
    [TestCase(80f, 132f, 172f, 0f)]
    [TestCase(-5f, 440f, 172f, 0f)]
    public void DetailRebuild_KeepsOffsetWithinTheUpdatedContentRange(float offset, float contentHeight,
        float viewportHeight, float expected)
    {
        Assert.That(PartitionViewRefreshCoordinator.ClampScrollOffset(offset, contentHeight, viewportHeight),
            Is.EqualTo(expected));
    }

    [Test]
    public void NonFiniteScrollDimensions_ResetToTheTop()
    {
        Assert.That(PartitionViewRefreshCoordinator.ClampScrollOffset(float.NaN, 440f, 172f), Is.Zero);
        Assert.That(PartitionViewRefreshCoordinator.ClampScrollOffset(80f, float.PositiveInfinity, 172f), Is.Zero);
        Assert.That(PartitionViewRefreshCoordinator.ClampScrollOffset(80f, 440f, float.NaN), Is.Zero);
    }

    [Test]
    public void MissingDetailScreen_RoutesRefreshToOverview()
    {
        var overviewRefreshes = 0;
        var detailRefreshes = 0;

        PartitionViewRefreshCoordinator.Refresh(hasDetailDraft: true, detailScreenVisible: false,
            () => overviewRefreshes++, () => detailRefreshes++);

        Assert.That(overviewRefreshes, Is.EqualTo(1));
        Assert.That(detailRefreshes, Is.Zero);
    }
}
