using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Tooltips;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Tooltips;

[TestFixture]
public sealed class TooltipLayoutTests
{
    private static readonly TooltipRect Viewport = new(0, 0, 400, 300);

    [Test]
    public void Arrange_BelowAnchor_UsesOneGroupDirection()
    {
        var result = Arrange(new TooltipRect(180, 120, 20, 20),
            new TooltipPanelMeasure("first", 10, 100, 30), new TooltipPanelMeasure("second", 20, 80, 40));

        Assert.That(result.Direction, Is.EqualTo(TooltipDirection.Below));
        Assert.That(result.Panels[1].Bounds.Y, Is.GreaterThan(result.Panels[0].Bounds.Y));
        Assert.That(result.Panels[1].Bounds.Bottom, Is.EqualTo(result.Panels[0].Bounds.Bottom + 42).Within(0.001));
    }

    [Test]
    public void Arrange_NearBottom_PlacesLowOrderNearAnchor()
    {
        var result = Arrange(new TooltipRect(180, 260, 20, 20),
            new TooltipPanelMeasure("a", 0, 100, 50), new TooltipPanelMeasure("b", 1, 100, 60));

        Assert.That(result.Direction, Is.EqualTo(TooltipDirection.Above));
        Assert.That(result.Panels[0].Bounds.Bottom, Is.EqualTo(result.ViewportBounds.Bottom).Within(0.001));
        Assert.That(result.Panels[0].Bounds.Y, Is.GreaterThan(result.Panels[1].Bounds.Y));
        Assert.That(result.Panels[0].Bounds.X, Is.EqualTo(result.Panels[1].Bounds.X));
    }

    [Test]
    public void Arrange_OverflowPrefersMaximumVisibleHeightWithoutOverlap()
    {
        var result = TooltipLayoutEngine.Arrange(new TooltipRect(180, 140, 20, 20), new TooltipRect(0, 0, 400, 300),
            new[] { new TooltipPanelMeasure("a", 0, 100, 250), new TooltipPanelMeasure("b", 1, 100, 250) },
            System.Array.Empty<TooltipRect>());

        Assert.That(result.IsClipped, Is.True);
        Assert.That(result.VisibleHeight, Is.EqualTo(292).Within(0.001));
        Assert.That(result.Overlap, Is.Zero);
        Assert.That(result.ViewportBounds.X, Is.GreaterThanOrEqualTo(4));
        Assert.That(result.ViewportBounds.Right, Is.LessThanOrEqualTo(396));
    }

    [Test]
    public void Arrange_WhenEverySideIsBlocked_UsesContainedMinimumOverlap()
    {
        var result = TooltipLayoutEngine.Arrange(new TooltipRect(180, 140, 20, 20), new TooltipRect(0, 0, 400, 300),
            new[] { new TooltipPanelMeasure("a", 0, 100, 250), new TooltipPanelMeasure("b", 1, 100, 250) },
            new[] { new TooltipRect(0, 0, 400, 300) });

        Assert.That(result.IsClipped, Is.True);
        Assert.That(result.ViewportBounds.X, Is.GreaterThanOrEqualTo(4));
        Assert.That(result.ViewportBounds.Right, Is.LessThanOrEqualTo(396));
    }

    [Test]
    public void Arrange_RightEdgeClampsFullyContainedCandidate()
    {
        var result = TooltipLayoutEngine.Arrange(new TooltipRect(380, 120, 10, 10), new TooltipRect(0, 0, 400, 300),
            new[] { new TooltipPanelMeasure("a", 0, 100, 30) }, System.Array.Empty<TooltipRect>());

        Assert.That(result.IsClipped, Is.False);
        Assert.That(result.Panels[0].Bounds.Right, Is.LessThanOrEqualTo(396));
        Assert.That(result.Panels[0].Bounds.X, Is.GreaterThanOrEqualTo(4));
    }

    [TestCase((int)TooltipDirection.Right)]
    [TestCase((int)TooltipDirection.Left)]
    public void Arrange_VerticalSpaceAvailable_ReplacesPreviousSideDirection(int previous)
    {
        var anchor = new TooltipRect(180, 120, 20, 20);
        var result = Arrange(anchor, new TooltipPanelMeasure("a", 0, 100, 30), previous: (TooltipDirection)previous);

        Assert.That(result.Direction, Is.EqualTo(TooltipDirection.Below));
        Assert.That(result.ViewportBounds.Y, Is.EqualTo(anchor.Bottom + TooltipLayoutEngine.DefaultGap));
    }

    [Test]
    public void Arrange_VerticalSpaceAvailable_PreservesPreviousAboveDirection()
    {
        var anchor = new TooltipRect(180, 120, 20, 20);
        var result = Arrange(anchor, new TooltipPanelMeasure("a", 0, 100, 30), previous: TooltipDirection.Above);

        Assert.That(result.Direction, Is.EqualTo(TooltipDirection.Above));
        Assert.That(result.ViewportBounds.Bottom, Is.EqualTo(anchor.Y - TooltipLayoutEngine.DefaultGap));
    }

    [TestCase((int)TooltipDirection.Right)]
    [TestCase((int)TooltipDirection.Left)]
    public void Arrange_VerticalPathsBlocked_PreservesPreviousSideDirection(int previous)
    {
        var anchor = new TooltipRect(180, 120, 20, 20);
        var obstacles = new[] { new TooltipRect(180, 0, 20, 120), new TooltipRect(180, 140, 20, 160) };
        var result = Arrange(anchor, new TooltipPanelMeasure("a", 0, 100, 30),
            previous: (TooltipDirection)previous, obstacles: obstacles);

        Assert.That(result.Direction, Is.EqualTo((TooltipDirection)previous));
        Assert.That(result.Overlap, Is.Zero);
        Assert.That(result.IsClipped, Is.False);
    }

    [TestCase((int)TooltipDirection.Right)]
    [TestCase((int)TooltipDirection.Left)]
    public void Arrange_OnlyAboveFits_ReplacesPreviousSideDirection(int previous)
    {
        var anchor = new TooltipRect(180, 260, 20, 20);
        var result = Arrange(anchor, new TooltipPanelMeasure("a", 0, 100, 50),
            new TooltipPanelMeasure("b", 1, 100, 60), previous: (TooltipDirection)previous);

        Assert.That(result.Direction, Is.EqualTo(TooltipDirection.Above));
        Assert.That(result.Panels[0].Bounds.Bottom, Is.EqualTo(anchor.Y - TooltipLayoutEngine.DefaultGap));
        Assert.That(result.Panels[1].Bounds.Bottom, Is.LessThan(result.Panels[0].Bounds.Y));
    }

    [Test]
    public void Arrange_PopupSideFits_PrefersRightOverPreviousBelow()
    {
        var popup = new TooltipRect(120, 100, 150, 100);
        var result = Arrange(popup, new TooltipPanelMeasure("a", 0, 100, 40), previous: TooltipDirection.Below,
            obstacles: new[] { popup }, preferHorizontal: true);

        Assert.That(result.Direction, Is.EqualTo(TooltipDirection.Right));
        Assert.That(result.ViewportBounds.X, Is.EqualTo(popup.Right + TooltipLayoutEngine.DefaultGap));
        Assert.That(result.Overlap, Is.Zero);
    }

    [Test]
    public void Arrange_PopupRightBlockedByNativeTooltip_UsesLeftSide()
    {
        var popup = new TooltipRect(120, 100, 150, 100);
        var native = new TooltipRect(275, 100, 80, 60);
        var result = Arrange(popup, new TooltipPanelMeasure("a", 0, 100, 40), previous: TooltipDirection.Right,
            obstacles: new[] { popup, native }, preferHorizontal: true);

        Assert.That(result.Direction, Is.EqualTo(TooltipDirection.Left));
        Assert.That(result.ViewportBounds.Right, Is.EqualTo(popup.X - TooltipLayoutEngine.DefaultGap));
        Assert.That(result.Overlap, Is.Zero);
    }

    [TestCase(true, 70, (int)TooltipDirection.Right, 102, false)]
    [TestCase(true, 215, (int)TooltipDirection.Right, 245, false)]
    [TestCase(false, 70, (int)TooltipDirection.Below, 278, true)]
    public void Arrange_SideHasRoomBelowNativeTooltip_ShiftsOnlyForPopupPreference(bool preferHorizontal,
        double obstacleHeight, int direction, double y, bool clipped)
    {
        var viewport = new TooltipRect(0, 0, 480, 310);
        var popup = new TooltipRect(4, 30, 320, 246);
        var native = new TooltipRect(326, 30, 120, obstacleHeight);
        var result = TooltipLayoutEngine.Arrange(popup, viewport, new[] { new TooltipPanelMeasure("a", 0, 120, 60) },
            new[] { popup, native }, preferHorizontal: preferHorizontal);

        Assert.That(result.Direction, Is.EqualTo((TooltipDirection)direction));
        Assert.That(result.ViewportBounds.Y, Is.EqualTo(y));
        Assert.That(result.IsClipped, Is.EqualTo(clipped));
        Assert.That(result.Overlap, Is.Zero);
    }

    [Test]
    public void Arrange_PopupSidesClampIntoPopup_UsesVerticalSpace()
    {
        var popup = new TooltipRect(40, 100, 320, 100);
        var result = Arrange(popup, new TooltipPanelMeasure("a", 0, 100, 40), previous: TooltipDirection.Right,
            obstacles: new[] { popup }, preferHorizontal: true);

        Assert.That(result.Direction, Is.EqualTo(TooltipDirection.Below));
        Assert.That(result.ViewportBounds.Y, Is.EqualTo(popup.Bottom + TooltipLayoutEngine.DefaultGap));
        Assert.That(result.Overlap, Is.Zero);
    }

    [Test]
    public void Arrange_UsesAvailableHeightAndKeepsPanelOrder()
    {
        var result = Arrange(new TooltipRect(180, 120, 20, 20),
            new TooltipPanelMeasure("a", 0, 100, 200), new TooltipPanelMeasure("b", 1, 100, 200),
            maxVisibleHeight: 100);

        Assert.That(result.IsClipped, Is.True);
        Assert.That(result.VisibleHeight, Is.EqualTo(100));
        Assert.That(result.Panels[1].Bounds.Y, Is.GreaterThan(result.Panels[0].Bounds.Y));
    }

    [Test]
    public void CoordinateMapper_ConvertsUnityTopLeftSpaceToDownwardLayout()
    {
        var local = new TooltipRect(12, -180, 40, 30);

        var layout = TooltipCoordinateMapper.ToTopLeft(local, 0, 0);

        Assert.That(layout.X, Is.EqualTo(12));
        Assert.That(layout.Y, Is.EqualTo(150));
        Assert.That(layout.Width, Is.EqualTo(40));
        Assert.That(layout.Height, Is.EqualTo(30));
    }

    [Test]
    public void Arrange_ConsidersObstacleAndChoosesOtherSide()
    {
        var anchor = new TooltipRect(180, 120, 20, 20);
        var result = Arrange(anchor, new TooltipPanelMeasure("a", 0, 100, 40),
            obstacles: new[] { new TooltipRect(180, 142, 100, 40) });
        Assert.That(result.Direction, Is.Not.EqualTo(TooltipDirection.Below));
    }

    private static TooltipLayoutResult Arrange(TooltipRect anchor, TooltipPanelMeasure first,
        TooltipPanelMeasure? second = null, TooltipDirection? previous = null,
        TooltipRect[]? obstacles = null, double maxVisibleHeight = double.PositiveInfinity, bool preferHorizontal = false)
    {
        var panels = second.HasValue ? new[] { first, second.Value } : new[] { first };
        return TooltipLayoutEngine.Arrange(anchor, Viewport, panels, obstacles ?? System.Array.Empty<TooltipRect>(),
            previous, maxVisibleHeight: maxVisibleHeight, preferHorizontal: preferHorizontal);
    }
}

[TestFixture]
public sealed class TooltipLayoutFrameGateTests
{
    [Test]
    public void FlushBeforeInput_UpdatesOverflowOnce_AndAllowsLaterSameFrameChanges()
    {
        var gate = new TooltipLayoutFrameGate();
        var scroll = new TooltipScrollState();

        Assert.That(gate.TryBegin(17), Is.True);
        Assert.That(gate.IsValid, Is.False, "scroll state is stale until layout completes");
        scroll.Update("first", 100, 40, 12);
        gate.Complete();
        Assert.That(gate.IsValid, Is.True);
        Assert.That(scroll.IsOverflowing, Is.True);
        Assert.That(gate.TryBegin(17), Is.False, "LateUpdate must not repeat the input-time layout");

        scroll.Reset("second");
        gate.Invalidate();
        Assert.That(gate.IsValid, Is.False, "a source change invalidates the old overflow range");
        Assert.That(gate.TryBegin(17), Is.True, "a new source in the same frame must refresh scroll readiness");
        scroll.Update("second", 30, 40, 12);
        gate.Complete();
        Assert.That(scroll.IsOverflowing, Is.False);
        Assert.That(gate.TryBegin(17), Is.False);
        Assert.That(gate.TryBegin(18), Is.True);
    }
}

[TestFixture]
public sealed class TooltipScrollStateTests
{
    [Test]
    public void Scroll_UsesThreeLinesAndClampsAtBothBoundaries()
    {
        var state = new TooltipScrollState();
        state.Update("item", 100, 40, 5);
        state.ScrollNotches(1);
        Assert.That(state.Offset, Is.EqualTo(15));
        state.ScrollNotches(100);
        Assert.That(state.Offset, Is.EqualTo(60));
        state.ScrollNotches(-100);
        Assert.That(state.Offset, Is.Zero);
    }

    [Test]
    public void Update_SameSourcePreservesOffsetAndNewSourceResetsIt()
    {
        var state = new TooltipScrollState();
        state.Update("item", 100, 40, 5);
        state.ScrollNotches(1);
        state.Update("item", 90, 40, 5);
        Assert.That(state.Offset, Is.EqualTo(15));
        state.Update("other", 90, 40, 5);
        Assert.That(state.Offset, Is.Zero);
    }
}
