using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Tooltips;

/// <summary>Directions considered by the shared tooltip layout.</summary>
internal enum TooltipDirection
{
    Below,
    Above,
    Right,
    Left
}

/// <summary>Small BCL-only rectangle used by tooltip layout tests and platform adapters.</summary>
internal readonly struct TooltipRect : IEquatable<TooltipRect>
{
    public TooltipRect(double x, double y, double width, double height)
    {
        if (!IsFinite(x) || !IsFinite(y) || !IsFinite(width) || !IsFinite(height) || width < 0 || height < 0)
            throw new ArgumentOutOfRangeException();
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width * Height;

    public bool Intersects(TooltipRect other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
    public bool Contains(TooltipRect other) => other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;
    public TooltipRect Translate(double x, double y) => new(x, y, Width, Height);
    public TooltipRect Clip(TooltipRect bounds) => new(Math.Max(X, bounds.X), Math.Max(Y, bounds.Y),
        Math.Max(0, Math.Min(Right, bounds.Right) - Math.Max(X, bounds.X)),
        Math.Max(0, Math.Min(Bottom, bounds.Bottom) - Math.Max(Y, bounds.Y)));
    public bool Equals(TooltipRect other) => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);
    public override bool Equals(object? obj) => obj is TooltipRect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>Maps Unity parent-local Y-up rectangles to the layout's top-left Y-down coordinates.</summary>
internal static class TooltipCoordinateMapper
{
    public static TooltipRect ToTopLeft(TooltipRect localBounds, double parentLeft, double parentTop) =>
        new(localBounds.X - parentLeft, parentTop - localBounds.Bottom, localBounds.Width, localBounds.Height);
}

/// <summary>Measured panel dimensions in the common layout parent.</summary>
internal readonly struct TooltipPanelMeasure
{
    public TooltipPanelMeasure(string id, int order, double width, double height)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height) || double.IsInfinity(width) || double.IsInfinity(height))
            throw new ArgumentOutOfRangeException();
        Order = order;
        Width = width;
        Height = height;
    }

    public string Id { get; }
    public int Order { get; }
    public double Width { get; }
    public double Height { get; }
}

/// <summary>Placement of one panel after the group has been laid out.</summary>
internal readonly struct TooltipPanelPlacement
{
    public TooltipPanelPlacement(string id, TooltipRect bounds)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Bounds = bounds;
    }

    public string Id { get; }
    public TooltipRect Bounds { get; }
}

/// <summary>Result of arranging all panels as one group.</summary>
internal sealed class TooltipLayoutResult
{
    internal TooltipLayoutResult(TooltipDirection direction, IReadOnlyList<TooltipPanelPlacement> panels,
        double visibleHeight, bool clipped, double overlap, double contentHeight, TooltipRect viewportBounds)
    {
        Direction = direction;
        Panels = panels;
        VisibleHeight = visibleHeight;
        IsClipped = clipped;
        Overlap = overlap;
        ContentHeight = contentHeight;
        ViewportBounds = viewportBounds;
    }

    public TooltipDirection Direction { get; }
    public IReadOnlyList<TooltipPanelPlacement> Panels { get; }
    public double VisibleHeight { get; }
    public bool IsClipped { get; }
    public double Overlap { get; }
    public double ContentHeight { get; }
    public TooltipRect ViewportBounds { get; }
}

/// <summary>Deterministic group layout with full-fit, overflow and minimum-overlap fallback.</summary>
internal static class TooltipLayoutEngine
{
    public const double DefaultGap = 2;
    public const double DefaultMargin = 4;

    public static TooltipLayoutResult Arrange(TooltipRect anchor, TooltipRect viewport,
        IReadOnlyList<TooltipPanelMeasure> panels, IReadOnlyList<TooltipRect> obstacles,
        TooltipDirection? previousDirection = null, double gap = DefaultGap, double margin = DefaultMargin,
        double maxVisibleHeight = double.PositiveInfinity, bool preferHorizontal = false)
    {
        if (panels == null || panels.Count == 0) throw new ArgumentException("At least one panel is required.", nameof(panels));
        if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
        if (viewport.Width <= 0 || viewport.Height <= 0) throw new ArgumentOutOfRangeException(nameof(viewport));
        if (gap < 0 || margin < 0 || double.IsNaN(maxVisibleHeight) || maxVisibleHeight <= 0)
            throw new ArgumentOutOfRangeException();

        IReadOnlyList<TooltipPanelMeasure> ordered = panels;
        for (var index = 1; index < panels.Count; index++)
        {
            if (Compare(panels[index - 1], panels[index]) <= 0) continue;
            var sorted = new List<TooltipPanelMeasure>(panels);
            sorted.Sort(Compare);
            ordered = sorted;
            break;
        }
        var contentHeight = 0d;
        var naturalWidth = 0d;
        for (var index = 0; index < ordered.Count; index++)
        {
            var panel = ordered[index];
            contentHeight += panel.Height;
            naturalWidth = Math.Max(naturalWidth, panel.Width);
        }

        contentHeight += gap * Math.Max(0, panels.Count - 1);

        Candidate? fullFit = null;
        Candidate? collisionFree = null;
        Candidate? fallback = null;
        for (var rank = 0; rank < 4; rank++)
        {
            var candidate = BuildCandidate((TooltipDirection)rank, anchor, viewport, contentHeight, naturalWidth,
                obstacles, gap, margin, maxVisibleHeight);
            if (candidate.IsFullFit && (!fullFit.HasValue || IsPreferred(candidate, fullFit.Value, previousDirection, preferHorizontal)))
                fullFit = candidate;
            if (candidate.Overlap <= 0.0001 && (!collisionFree.HasValue || candidate.VisibleHeight > collisionFree.Value.VisibleHeight))
                collisionFree = candidate;
            if (!fallback.HasValue || candidate.Overlap < fallback.Value.Overlap ||
                (candidate.Overlap.Equals(fallback.Value.Overlap) && candidate.VisibleHeight > fallback.Value.VisibleHeight))
                fallback = candidate;
        }

        if (preferHorizontal && (fullFit == null || fullFit.Value.Direction is TooltipDirection.Below or TooltipDirection.Above))
        {
            fullFit = FindShiftedSide(gap) ?? (gap > 0 ? FindShiftedSide(0) : null) ?? fullFit;
        }

        var selected = (fullFit ?? collisionFree ?? fallback)!.Value;
        var placements = new TooltipPanelPlacement[ordered.Count];
        var offset = 0d;
        for (var index = 0; index < ordered.Count; index++)
        {
            var panel = ordered[index];
            var y = selected.Direction == TooltipDirection.Above
                ? selected.Bounds.Bottom - offset - panel.Height
                : selected.Bounds.Y + offset;
            placements[index] = new TooltipPanelPlacement(panel.Id,
                new TooltipRect(selected.Bounds.X, y, selected.Bounds.Width, panel.Height));
            offset += panel.Height + gap;
        }
        return new TooltipLayoutResult(selected.Direction, placements, selected.VisibleHeight,
            contentHeight > selected.VisibleHeight + 0.0001, selected.Overlap, contentHeight, selected.Bounds);

        Candidate? FindShiftedSide(double obstacleGap)
        {
            Candidate? best = null;
            for (var rank = (int)TooltipDirection.Right; rank <= (int)TooltipDirection.Left; rank++)
            {
                var candidate = BuildCandidate((TooltipDirection)rank, anchor, viewport, contentHeight, naturalWidth,
                    obstacles, gap, margin, maxVisibleHeight);
                var shifted = ShiftSideCandidate(candidate, anchor, viewport, obstacles, contentHeight, gap, margin, obstacleGap);
                if (shifted is { } fit && (!best.HasValue || IsPreferred(fit, best.Value, previousDirection, preferHorizontal)))
                    best = fit;
            }
            return best;
        }
    }

    internal static int Compare(TooltipPanelMeasure left, TooltipPanelMeasure right) => left.Order != right.Order
        ? left.Order.CompareTo(right.Order)
        : string.CompareOrdinal(left.Id, right.Id);

    private static bool IsPreferred(Candidate candidate, Candidate current, TooltipDirection? previous, bool preferHorizontal)
    {
        var candidateVertical = candidate.Direction is TooltipDirection.Below or TooltipDirection.Above;
        var currentVertical = current.Direction is TooltipDirection.Below or TooltipDirection.Above;
        if (candidateVertical != currentVertical) return candidateVertical != preferHorizontal;
        if (previous.HasValue)
        {
            var candidatePrevious = candidate.Direction == previous.Value;
            var currentPrevious = current.Direction == previous.Value;
            if (candidatePrevious != currentPrevious) return candidatePrevious;
        }
        return (int)candidate.Direction < (int)current.Direction;
    }

    private static Candidate BuildCandidate(TooltipDirection direction, TooltipRect anchor, TooltipRect viewport,
        double contentHeight, double naturalWidth, IReadOnlyList<TooltipRect> obstacles,
        double gap, double margin, double maxVisibleHeight)
    {
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

        var overlap = MeasureOverlap(group, obstacles);
        var fullFit = visibleHeight.Equals(contentHeight) && overlap <= 0 && viewport.Contains(group);
        return new Candidate(direction, visibleHeight, overlap, fullFit, group);
    }

    private static Candidate? ShiftSideCandidate(Candidate candidate, TooltipRect anchor, TooltipRect viewport,
        IReadOnlyList<TooltipRect> obstacles, double contentHeight, double gap, double margin, double obstacleGap)
    {
        var bounds = candidate.Bounds;
        if (!bounds.Height.Equals(contentHeight)) return null;
        if (candidate.Direction == TooltipDirection.Right ? bounds.X < anchor.Right + gap : bounds.Right > anchor.X - gap)
            return null;

        var inset = Math.Min(margin, viewport.Height / 2d);
        var nearestDistance = double.PositiveInfinity;
        Candidate? nearest = null;
        foreach (var obstacle in obstacles)
        {
            if (obstacle.Right <= bounds.X || obstacle.X >= bounds.Right) continue;
            Consider(obstacle.Y - obstacleGap - bounds.Height);
            Consider(obstacle.Bottom + obstacleGap);
        }
        return nearest;

        void Consider(double y)
        {
            if (y < viewport.Y + inset || y + bounds.Height > viewport.Bottom - inset) return;
            var distance = Math.Abs(y - bounds.Y);
            if (distance >= nearestDistance) return;
            var shifted = new TooltipRect(bounds.X, y, bounds.Width, bounds.Height);
            if (MeasureOverlap(shifted, obstacles) > 0) return;
            nearestDistance = distance;
            nearest = new Candidate(candidate.Direction, candidate.VisibleHeight, overlap: 0, fullFit: true, shifted);
        }
    }

    private static double MeasureOverlap(TooltipRect bounds, IReadOnlyList<TooltipRect> obstacles)
    {
        var overlap = 0d;
        for (var index = 0; index < obstacles.Count; index++) overlap += bounds.Clip(obstacles[index]).Area;
        return overlap;
    }

    private static double Clamp(double value, double minimum, double maximum) => Math.Min(Math.Max(value, minimum), Math.Max(minimum, maximum));

    private readonly struct Candidate
    {
        internal Candidate(TooltipDirection direction, double visibleHeight, double overlap, bool fullFit, TooltipRect bounds)
        {
            Direction = direction;
            VisibleHeight = visibleHeight;
            Overlap = overlap;
            IsFullFit = fullFit;
            Bounds = bounds;
        }

        internal TooltipDirection Direction { get; }
        internal double VisibleHeight { get; }
        internal double Overlap { get; }
        internal bool IsFullFit { get; }
        internal TooltipRect Bounds { get; }
    }
}

/// <summary>Runs a visible group's layout once per frame unless a later same-frame input invalidates it.</summary>
internal sealed class TooltipLayoutFrameGate
{
    private int _frame = int.MinValue;
    private bool _dirty = true;

    internal bool IsValid { get; private set; }
    public void Invalidate()
    {
        _dirty = true;
        IsValid = false;
    }

    public bool TryBegin(int frame)
    {
        if (!_dirty && _frame == frame) return false;
        _frame = frame;
        _dirty = false;
        IsValid = false;
        return true;
    }

    public void Complete() => IsValid = true;
}

/// <summary>Scroll state shared by every panel in one tooltip group.</summary>
internal sealed class TooltipScrollState
{
    private double _offset;
    private string? _source;
    public double Offset => _offset;
    public double MaximumOffset { get; private set; }

    public void Update(string source, double contentHeight, double viewportHeight, double lineHeight)
    {
        if (!string.Equals(_source, source, StringComparison.Ordinal)) _offset = 0;
        _source = source;
        MaximumOffset = Math.Max(0, contentHeight - viewportHeight);
        _offset = Math.Min(_offset, MaximumOffset);
        LineHeight = lineHeight > 0 ? lineHeight : 1;
    }

    public double LineHeight { get; private set; } = 1;
    public bool IsOverflowing => MaximumOffset > 0;
    public void Reset(string source) { _source = source; _offset = 0; MaximumOffset = 0; }
    public void ScrollNotches(double notches) => _offset = Math.Max(0, Math.Min(MaximumOffset, _offset + notches * 3 * LineHeight));
}
