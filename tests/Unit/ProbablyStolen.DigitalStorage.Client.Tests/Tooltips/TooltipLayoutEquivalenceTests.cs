using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Tooltips;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Tooltips;

[TestFixture]
public sealed class TooltipLayoutEquivalenceTests
{
    [Test]
    public void Arrange_SeededGeometry_MatchesPreOptimizationPolicyAndPlacements()
    {
        var random = new Random(1731);
        for (var sample = 0; sample < 600; sample++)
        {
            var viewport = new TooltipRect(-15, 12, random.Next(2, 900), random.Next(2, 700));
            var anchor = new TooltipRect(random.Next(-40, 900), random.Next(-40, 700), random.Next(0, 100), random.Next(0, 100));
            var panels = new TooltipPanelMeasure[random.Next(1, 8)];
            for (var index = 0; index < panels.Length; index++)
                panels[index] = new TooltipPanelMeasure($"panel-{index}", random.Next(-2, 3), random.Next(1, 1000), random.Next(1, 800));
            var obstacles = new TooltipRect[random.Next(0, 6)];
            for (var index = 0; index < obstacles.Length; index++)
                obstacles[index] = new TooltipRect(random.Next(-40, 900), random.Next(-40, 700), random.Next(0, 800), random.Next(0, 800));
            var previous = sample % 5 == 0 ? (TooltipDirection?)null : (TooltipDirection)(sample % 4);
            var maxHeight = sample % 3 == 0 ? random.Next(1, 600) : double.PositiveInfinity;
            var expected = TooltipLayoutReference.Arrange(anchor, viewport, panels, obstacles, previous, maxVisibleHeight: maxHeight);
            var actual = TooltipLayoutEngine.Arrange(anchor, viewport, panels, obstacles, previous, maxVisibleHeight: maxHeight);
            AssertEquivalent(expected, actual, sample);
        }
    }

    [Test]
    public void Arrange_SortedFastPath_MatchesUnsortedInput_AndDoesNotReusePriorPlacements()
    {
        var panels = new[] { new TooltipPanelMeasure("b", 2, 120, 50), new TooltipPanelMeasure("a", 1, 80, 60) };
        var originalIds = panels.Select(panel => panel.Id).ToArray();
        var anchor = new TooltipRect(120, 240, 30, 20);
        var viewport = new TooltipRect(0, 0, 400, 300);
        var unsorted = TooltipLayoutEngine.Arrange(anchor, viewport, panels, Array.Empty<TooltipRect>());
        var retainedBounds = unsorted.Panels.Select(panel => panel.Bounds).ToArray();
        var ordered = panels.OrderBy(panel => panel.Order).ToArray();
        var sorted = TooltipLayoutEngine.Arrange(anchor, viewport, ordered, Array.Empty<TooltipRect>());
        AssertEquivalent(unsorted, sorted, 0);
        TooltipLayoutEngine.Arrange(new TooltipRect(20, 20, 5, 5), viewport, ordered, Array.Empty<TooltipRect>());
        Assert.That(unsorted.Panels.Select(panel => panel.Bounds), Is.EqualTo(retainedBounds));
        Assert.That(panels.Select(panel => panel.Id), Is.EqualTo(originalIds));
    }

    private static void AssertEquivalent(TooltipLayoutResult expected, TooltipLayoutResult actual, int sample)
    {
        Assert.That(actual.Direction, Is.EqualTo(expected.Direction), $"direction in sample {sample}");
        Assert.That(actual.ViewportBounds, Is.EqualTo(expected.ViewportBounds), $"viewport in sample {sample}");
        Assert.That(actual.ContentHeight, Is.EqualTo(expected.ContentHeight));
        Assert.That(actual.VisibleHeight, Is.EqualTo(expected.VisibleHeight));
        Assert.That(actual.IsClipped, Is.EqualTo(expected.IsClipped));
        Assert.That(actual.Overlap, Is.EqualTo(expected.Overlap));
        Assert.That(actual.Panels.Select(panel => (panel.Id, panel.Bounds)),
            Is.EqualTo(expected.Panels.Select(panel => (panel.Id, panel.Bounds))));
    }
}

// Frozen pre-remediation Client algorithm: differential evidence for the candidate/allocation rewrite.
internal static class TooltipLayoutReference
{
    public const double DefaultGap = 2;
    public const double DefaultMargin = 4;

    public static TooltipLayoutResult Arrange(TooltipRect anchor, TooltipRect viewport,
        IReadOnlyList<TooltipPanelMeasure> panels, IReadOnlyList<TooltipRect> obstacles,
        TooltipDirection? previousDirection = null, double gap = DefaultGap, double margin = DefaultMargin,
        double maxVisibleHeight = double.PositiveInfinity)
    {
        if (panels == null || panels.Count == 0) throw new ArgumentException("At least one panel is required.", nameof(panels));
        if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
        if (viewport.Width <= 0 || viewport.Height <= 0) throw new ArgumentOutOfRangeException(nameof(viewport));
        if (gap < 0 || margin < 0 || double.IsNaN(maxVisibleHeight) || maxVisibleHeight <= 0)
            throw new ArgumentOutOfRangeException();

        var ordered = new List<TooltipPanelMeasure>(panels);
        ordered.Sort((left, right) => left.Order != right.Order
            ? left.Order.CompareTo(right.Order)
            : string.CompareOrdinal(left.Id, right.Id));
        var directions = new[] { TooltipDirection.Below, TooltipDirection.Above, TooltipDirection.Right, TooltipDirection.Left };
        var candidates = directions.Select(direction => BuildCandidate(direction, anchor, viewport, ordered, obstacles,
            gap, margin, maxVisibleHeight)).ToArray();

        Candidate? selected = null;
        foreach (var candidate in candidates)
        {
            if (!candidate.IsFullFit) continue;
            if (selected == null || IsPreferred(candidate, selected, previousDirection)) selected = candidate;
        }

        if (selected == null)
        {
            selected = candidates.Where(candidate => candidate.Overlap <= 0.0001)
                .OrderByDescending(candidate => candidate.VisibleHeight)
                .ThenBy(candidate => candidate.Rank)
                .FirstOrDefault();
        }

        if (selected == null)
        {
            selected = candidates.OrderBy(candidate => candidate.Overlap)
                .ThenByDescending(candidate => candidate.VisibleHeight)
                .ThenBy(candidate => candidate.Rank)
                .First();
        }

        return selected.ToResult();
    }

    private static bool IsPreferred(Candidate candidate, Candidate current, TooltipDirection? previous)
    {
        var candidateVertical = candidate.Direction is TooltipDirection.Below or TooltipDirection.Above;
        var currentVertical = current.Direction is TooltipDirection.Below or TooltipDirection.Above;
        if (candidateVertical != currentVertical) return candidateVertical;
        if (previous.HasValue)
        {
            var candidatePrevious = candidate.Direction == previous.Value;
            var currentPrevious = current.Direction == previous.Value;
            if (candidatePrevious != currentPrevious) return candidatePrevious;
        }
        return candidate.Rank < current.Rank;
    }

    private static Candidate BuildCandidate(TooltipDirection direction, TooltipRect anchor, TooltipRect viewport,
        IReadOnlyList<TooltipPanelMeasure> panels, IReadOnlyList<TooltipRect> obstacles,
        double gap, double margin, double maxVisibleHeight)
    {
        var contentHeight = 0d;
        var naturalWidth = 0d;
        foreach (var panel in panels)
        {
            contentHeight += panel.Height;
            naturalWidth = Math.Max(naturalWidth, panel.Width);
        }
        contentHeight += gap * Math.Max(0, panels.Count - 1);

        var insetX = Math.Min(margin, viewport.Width / 2d);
        var insetY = Math.Min(margin, viewport.Height / 2d);
        var left = viewport.X + insetX;
        var bottom = viewport.Y + insetY;
        var usableWidth = Math.Max(1, viewport.Width - insetX * 2);
        var usableHeight = Math.Max(1, viewport.Height - insetY * 2);
        var width = Math.Max(1, Math.Min(naturalWidth, usableWidth));
        var maxHeight = Math.Min(contentHeight, Math.Min(usableHeight, maxVisibleHeight));
        var available = direction == TooltipDirection.Below
            ? viewport.Bottom - insetY - anchor.Bottom - gap
            : direction == TooltipDirection.Above
                ? anchor.Y - viewport.Y - insetY - gap
                : usableHeight;
        var visibleHeight = Math.Max(1, Math.Min(maxHeight, Math.Max(1, available)));

        var x = direction == TooltipDirection.Right ? anchor.Right + gap : direction == TooltipDirection.Left ? anchor.X - width - gap : anchor.X;
        var y = direction == TooltipDirection.Below ? anchor.Bottom + gap
            : direction == TooltipDirection.Above ? anchor.Y - gap - visibleHeight
            : anchor.Y;
        x = Clamp(x, left, left + usableWidth - width);
        y = Clamp(y, bottom, bottom + usableHeight - visibleHeight);
        var group = new TooltipRect(x, y, width, visibleHeight);

        var overlap = 0d;
        foreach (var obstacle in obstacles) overlap += group.Clip(obstacle).Area;
        var placements = new List<TooltipPanelPlacement>(panels.Count);
        var offset = 0d;
        foreach (var panel in panels)
        {
            var panelY = direction == TooltipDirection.Above
                ? y + visibleHeight - offset - panel.Height
                : y + offset;
            placements.Add(new TooltipPanelPlacement(panel.Id, new TooltipRect(x, panelY, width, panel.Height)));
            offset += panel.Height + gap;
        }

        var fullFit = !visibleHeight.Equals(contentHeight) ? false : overlap <= 0 && viewport.Contains(group);
        return new Candidate(direction, placements, visibleHeight, contentHeight > visibleHeight + 0.0001, overlap,
            fullFit, contentHeight, group, Array.IndexOf(directions, direction));
    }

    private static readonly TooltipDirection[] directions = { TooltipDirection.Below, TooltipDirection.Above, TooltipDirection.Right, TooltipDirection.Left };

    private static double Clamp(double value, double minimum, double maximum) => Math.Min(Math.Max(value, minimum), Math.Max(minimum, maximum));

    private sealed class Candidate
    {
        internal Candidate(TooltipDirection direction, IReadOnlyList<TooltipPanelPlacement> panels, double visibleHeight,
            bool clipped, double overlap, bool fullFit, double contentHeight, TooltipRect viewportBounds, int rank)
        {
            Direction = direction;
            Panels = panels;
            VisibleHeight = visibleHeight;
            IsClipped = clipped;
            Overlap = overlap;
            IsFullFit = fullFit;
            ContentHeight = contentHeight;
            ViewportBounds = viewportBounds;
            Rank = rank;
        }

        internal TooltipDirection Direction { get; }
        internal IReadOnlyList<TooltipPanelPlacement> Panels { get; }
        internal double VisibleHeight { get; }
        internal bool IsClipped { get; }
        internal double Overlap { get; }
        internal bool IsFullFit { get; }
        internal double ContentHeight { get; }
        internal TooltipRect ViewportBounds { get; }
        internal int Rank { get; }
        internal TooltipLayoutResult ToResult() => new(Direction, Panels, VisibleHeight, IsClipped, Overlap, ContentHeight, ViewportBounds);
    }
}
