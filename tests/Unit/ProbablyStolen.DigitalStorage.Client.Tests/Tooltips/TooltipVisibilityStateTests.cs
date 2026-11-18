using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Client.Workbench;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Tooltips;

[TestFixture]
public sealed class TooltipVisibilityStateTests
{
    [TestCase(-1, null, true)]
    [TestCase(3, "", true)]
    [TestCase(3, "rule", false)]
    public void HoverThenExitEmptyOrDrag_FollowDoesNotReopen_AndStationaryRestoreWorks(int slot, string? text, bool canShow)
    {
        var visibility = new TooltipVisibilityState();
        var visible = false;
        var coordinator = new PartitionHoverHintCoordinator(_ => visibility.Show(), () => { visibility.Hide(); visible = false; });
        void Follow() { if (visibility.CanFollow) visible = true; }

        coordinator.Refresh(3, "rule", true);
        Follow();
        Assert.That(visible, Is.True);
        coordinator.Refresh(slot, text, canShow);
        Follow();
        Assert.That(visible, Is.False, "following cached content after Refresh must not undo Hide");

        coordinator.Refresh(3, "rule", true);
        Follow();
        Assert.That(visible, Is.True);
    }

    [Test]
    public void ExplicitHideAndGridRefresh_FollowStaysHiddenUntilTheCoordinatorRestoresContent()
    {
        var visibility = new TooltipVisibilityState();
        var coordinator = new PartitionHoverHintCoordinator(_ => visibility.Show(), visibility.Hide);
        coordinator.Refresh(3, "rule", true);
        coordinator.Hide();
        for (var frame = 0; frame < 10; frame++)
            Assert.That(visibility.CanFollow, Is.False);

        coordinator.Refresh(3, "rule", true);
        Assert.That(visibility.CanFollow, Is.True, "a stationary pointer can restore the same bound hint after a grid refresh");
        visibility.Hide();
        Assert.That(visibility.CanFollow, Is.False);
    }
}
