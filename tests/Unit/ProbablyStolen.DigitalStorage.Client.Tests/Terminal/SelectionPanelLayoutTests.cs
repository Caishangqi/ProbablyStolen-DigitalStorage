using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class SelectionPanelLayoutTests
{
    // Reference geometry: 18x20 buttons, gap 2, padding 3, and 2 extra height for border art.
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
    public void RowsAndColumns_MatchAe2wtlib(int count, int rows, int columns)
    {
        var panel = SelectionPanelLayout.Compute(count, Width, Height, Gap, Padding, extraHeight: 2);

        Assert.That(panel.Rows, Is.EqualTo(rows));
        Assert.That(panel.Columns, Is.EqualTo(columns));
        // panelWidth = PADDING*2 + columns*W + (columns-1)*GAP; panelHeight = PADDING*2 + 2 + rows*H + (rows-1)*GAP.
        Assert.That(panel.Width, Is.EqualTo(Padding * 2 + columns * Width + (columns - 1) * Gap));
        Assert.That(panel.Height, Is.EqualTo(Padding * 2 + 2 + rows * Height + (rows - 1) * Gap));
    }

    [Test]
    public void Buttons_FillColumnsTopToBottomAndCentreAShortLastColumn()
    {
        var panel = SelectionPanelLayout.Compute(4, Width, Height, Gap, Padding);

        Assert.That(new[] { panel.ButtonX(0), panel.ButtonY(0) }, Is.EqualTo(new[] { 3, 3 }));
        Assert.That(new[] { panel.ButtonX(2), panel.ButtonY(2) }, Is.EqualTo(new[] { 3, 3 + 2 * 22 }));
        // Index 3 is alone in the last column: offset (3 - 1) * 22 / 2 = 22 centres it on the middle row.
        Assert.That(new[] { panel.ButtonX(3), panel.ButtonY(3) }, Is.EqualTo(new[] { 3 + 20, 3 + 22 }));
    }

    [Test]
    public void Place_OpensLeftWithTopsAligned()
    {
        var panel = SelectionPanelLayout.Compute(2, 40, 40, 3, 6);
        var placement = SelectionPanelLayout.Place(panel, triggerX: 200, triggerY: 50, triggerWidth: 40,
            viewportLeft: 0, panelGap: 4);

        Assert.That(placement.Flipped, Is.False);
        Assert.That(placement.X, Is.EqualTo(200 - 4 - panel.Width));
        Assert.That(placement.Y, Is.EqualTo(50));
    }

    [Test]
    public void Place_FlipsRightWhenTheLeftSideLeavesTheViewport()
    {
        var panel = SelectionPanelLayout.Compute(2, 40, 40, 3, 6);
        var placement = SelectionPanelLayout.Place(panel, triggerX: 30, triggerY: 50, triggerWidth: 40,
            viewportLeft: 0, panelGap: 4);

        Assert.That(placement.Flipped, Is.True);
        Assert.That(placement.X, Is.EqualTo(30 + 40 + 4));
    }

    [Test]
    public void Compute_RejectsNegativeCounts() =>
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = SelectionPanelLayout.Compute(-1, 1, 1, 0, 0); }));
}
