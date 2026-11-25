using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class SelectionPanelLayoutTests
{
    private const int Width = 18;
    private const int Height = 20;
    private const int Gap = 2;
    private const int Padding = 3;

    [TestCase(0, 1, 1)]
    [TestCase(1, 1, 1)]
    [TestCase(3, 3, 1)]
    [TestCase(4, 3, 2)]
    [TestCase(6, 3, 2)]
    [TestCase(7, 3, 3)]
    public void Compute_PacksButtonsIntoColumnsOfAtMostThreeRows(int count, int rows, int columns)
    {
        var panel = SelectionPanelLayout.Compute(count, Width, Height, Gap, Padding, extraHeight: 2);

        Assert.That(panel.Rows, Is.EqualTo(rows));
        Assert.That(panel.Columns, Is.EqualTo(columns));
        Assert.That(panel.Width, Is.EqualTo(Padding * 2 + columns * Width + (columns - 1) * Gap));
        Assert.That(panel.Height, Is.EqualTo(Padding * 2 + 2 + rows * Height + (rows - 1) * Gap));
    }

    [Test]
    public void Buttons_FillColumnsTopToBottomAndCentreAShortLastColumn()
    {
        var panel = SelectionPanelLayout.Compute(4, Width, Height, Gap, Padding);

        Assert.That(new[] { panel.ButtonX(0), panel.ButtonY(0) }, Is.EqualTo(new[] { 3, 3 }));
        Assert.That(new[] { panel.ButtonX(2), panel.ButtonY(2) }, Is.EqualTo(new[] { 3, 3 + 2 * 22 }));
        Assert.That(new[] { panel.ButtonX(3), panel.ButtonY(3) }, Is.EqualTo(new[] { 3 + 20, 3 + 22 }));
    }

    [Test]
    public void Place_OpensLeftWithTopsAligned()
    {
        var panel = SelectionPanelLayout.Compute(2, 40, 40, 3, 6);
        var placement = SelectionPanelLayout.Place(panel, DockSide.Left, triggerX: 200, triggerY: 50, triggerWidth: 40,
            viewportLeft: 0, viewportRight: 1000, panelGap: 4);

        Assert.That(placement.Flipped, Is.False);
        Assert.That(placement.X, Is.EqualTo(200 - 4 - panel.Width));
        Assert.That(placement.Y, Is.EqualTo(50));
    }

    [Test]
    public void Place_FlipsRightWhenTheLeftSideLeavesTheViewport()
    {
        var panel = SelectionPanelLayout.Compute(2, 40, 40, 3, 6);
        var placement = SelectionPanelLayout.Place(panel, DockSide.Left, triggerX: 30, triggerY: 50, triggerWidth: 40,
            viewportLeft: 0, viewportRight: 1000, panelGap: 4);

        Assert.That(placement.Flipped, Is.True);
        Assert.That(placement.X, Is.EqualTo(30 + 40 + 4));
    }

    [Test]
    public void Place_RightDockOpensRightWithTopsAligned()
    {
        var panel = SelectionPanelLayout.Compute(2, 40, 40, 3, 6);
        var placement = SelectionPanelLayout.Place(panel, DockSide.Right, triggerX: 200, triggerY: 50, triggerWidth: 40,
            viewportLeft: 0, viewportRight: 1000, panelGap: 4);

        Assert.That(placement.Flipped, Is.False);
        Assert.That(placement.X, Is.EqualTo(200 + 40 + 4));
        Assert.That(placement.Y, Is.EqualTo(50));
    }

    [Test]
    public void Place_RightDockFlipsLeftWhenTheRightSideLeavesTheViewport()
    {
        var panel = SelectionPanelLayout.Compute(2, 40, 40, 3, 6);
        var placement = SelectionPanelLayout.Place(panel, DockSide.Right, triggerX: 900, triggerY: 50, triggerWidth: 40,
            viewportLeft: 0, viewportRight: 960, panelGap: 4);

        Assert.That(placement.Flipped, Is.True);
        Assert.That(placement.X, Is.EqualTo(900 - 4 - panel.Width));
    }

    [Test]
    public void Compute_RejectsNegativeCounts() =>
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = SelectionPanelLayout.Compute(-1, 1, 1, 0, 0); }));
}
