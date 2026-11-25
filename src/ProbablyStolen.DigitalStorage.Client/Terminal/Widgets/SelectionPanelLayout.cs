using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// Lays out a selection panel next to its button. Coordinates are in design units with the origin at the top left and
/// y growing downward.
/// </summary>
public static class SelectionPanelLayout
{
    /// <summary>The most buttons in one column; more start a new column.</summary>
    public const int MaxRows = 3;

    /// <summary>Computes the panel size and button offsets for <paramref name="count"/> buttons.</summary>
    /// <param name="count">The number of buttons, at least zero.</param>
    /// <param name="buttonWidth">The button width.</param>
    /// <param name="buttonHeight">The button height.</param>
    /// <param name="gap">The gap between buttons.</param>
    /// <param name="padding">The padding inside the panel border.</param>
    /// <param name="extraHeight">Extra height below the buttons, for border art.</param>
    public static PanelGeometry Compute(int count, int buttonWidth, int buttonHeight, int gap, int padding,
        int extraHeight = 0)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var rows = Math.Max(1, Math.Min(MaxRows, count));
        var columns = Math.Max(1, (count + rows - 1) / rows);
        var width = padding * 2 + columns * buttonWidth + Math.Max(0, columns - 1) * gap;
        var height = padding * 2 + extraHeight + rows * buttonHeight + Math.Max(0, rows - 1) * gap;
        return new PanelGeometry(count, rows, columns, width, height, buttonWidth, buttonHeight, gap, padding);
    }

    /// <summary>
    /// Places the panel outward from its dock with the top edges aligned: left of the button on the left dock, right of
    /// it on the right dock. When that leaves the viewport, it opens toward the window instead.
    /// </summary>
    /// <param name="panel">The computed panel.</param>
    /// <param name="side">The dock the button is on.</param>
    /// <param name="triggerX">The button's left edge.</param>
    /// <param name="triggerY">The button's top edge.</param>
    /// <param name="triggerWidth">The button width.</param>
    /// <param name="viewportLeft">The viewport's left edge in the same coordinates.</param>
    /// <param name="viewportRight">The viewport's right edge in the same coordinates.</param>
    /// <param name="panelGap">The gap between button and panel.</param>
    public static PanelPlacement Place(PanelGeometry panel, DockSide side, float triggerX, float triggerY,
        float triggerWidth, float viewportLeft, float viewportRight, float panelGap)
    {
        if (panel == null) throw new ArgumentNullException(nameof(panel));
        var left = triggerX - panelGap - panel.Width;
        var right = triggerX + triggerWidth + panelGap;
        if (side == DockSide.Left)
            return left >= viewportLeft ? new PanelPlacement(left, triggerY, false) : new PanelPlacement(right, triggerY, true);
        return right + panel.Width <= viewportRight
            ? new PanelPlacement(right, triggerY, false)
            : new PanelPlacement(left, triggerY, true);
    }
}

/// <summary>The size of a selection panel and where its buttons sit inside it.</summary>
public sealed class PanelGeometry
{
    private readonly int _buttonWidth;
    private readonly int _buttonHeight;
    private readonly int _gap;
    private readonly int _padding;

    internal PanelGeometry(int count, int rows, int columns, int width, int height, int buttonWidth, int buttonHeight,
        int gap, int padding)
    {
        Count = count;
        Rows = rows;
        Columns = columns;
        Width = width;
        Height = height;
        _buttonWidth = buttonWidth;
        _buttonHeight = buttonHeight;
        _gap = gap;
        _padding = padding;
    }

    /// <summary>Gets the number of buttons.</summary>
    public int Count { get; }

    /// <summary>Gets the rows per column.</summary>
    public int Rows { get; }

    /// <summary>Gets the number of columns.</summary>
    public int Columns { get; }

    /// <summary>Gets the panel width.</summary>
    public int Width { get; }

    /// <summary>Gets the panel height.</summary>
    public int Height { get; }

    /// <summary>Gets a button's left edge inside the panel; buttons fill columns top to bottom.</summary>
    public int ButtonX(int index) => _padding + index / Rows * (_buttonWidth + _gap);

    /// <summary>Gets a button's top edge inside the panel; a partly filled last column is centred vertically.</summary>
    public int ButtonY(int index) => _padding + index % Rows * (_buttonHeight + _gap) + LastColumnYOffset(index);

    private int LastColumnYOffset(int index)
    {
        var entries = Count % Rows;
        if (entries == 0 || index / Rows != Columns - 1) return 0;
        return (Rows - entries) * (_buttonHeight + _gap) / 2;
    }
}

/// <summary>Where a selection panel opens.</summary>
public sealed class PanelPlacement
{
    internal PanelPlacement(float x, float y, bool flipped)
    {
        X = x;
        Y = y;
        Flipped = flipped;
    }

    /// <summary>Gets the panel's left edge.</summary>
    public float X { get; }

    /// <summary>Gets the panel's top edge.</summary>
    public float Y { get; }

    /// <summary>Gets whether the panel opens toward the window because the outward side had no room.</summary>
    public bool Flipped { get; }
}
