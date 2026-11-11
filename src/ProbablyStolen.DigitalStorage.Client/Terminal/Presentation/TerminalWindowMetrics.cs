using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Defines the terminal's design coordinates, row count and viewport fit.</summary>
public static class TerminalWindowMetrics
{
    /// <summary>Gets the fixed number of columns.</summary>
    public const int Columns = 7;
    /// <summary>Gets the row count used before a viewport is known.</summary>
    public const int Rows = 4;
    /// <summary>Gets the minimum visible rows.</summary>
    public const int MinimumRows = 2;
    /// <summary>Gets the square slot size in design units.</summary>
    public const int CellPixels = 40;
    /// <summary>Gets the gap between slots in design units.</summary>
    public const int CellSpacing = 3;
    /// <summary>Gets the square toolbar button size in design units.</summary>
    public const int ToolbarButton = 28;
    /// <summary>Gets the gap between toolbar buttons in design units.</summary>
    public const int ToolbarSpacing = 4;
    /// <summary>Gets the number of left toolbar buttons.</summary>
    public const int ToolbarButtons = 5;
    /// <summary>Gets the scrollbar track width in design units.</summary>
    public const int ScrollbarWidth = 12;
    /// <summary>Gets the fixed scrollbar handle height in design units.</summary>
    public const int ScrollbarHandle = 30;
    /// <summary>Gets the outer width in design units: grid, toolbar and scrollbar plus window padding.</summary>
    public const int Width = 400;
    /// <summary>Gets the outer height that is not grid: title, search row, status row, padding and spacing.</summary>
    public const int Chrome = 169;
    /// <summary>Gets the outer height for <see cref="Rows"/>.</summary>
    public const int Height = Chrome + Rows * CellPixels + (Rows - 1) * CellSpacing;
    /// <summary>Gets the free space kept above and below the window when counting rows, in design units.</summary>
    public const int ViewportMargin = 40;
    /// <summary>Gets the correction from doubled machinery controls to native window coordinates.</summary>
    public const float NativeScale = 0.5f;
    /// <summary>Gets the maximum width as a fraction of the game viewport.</summary>
    public const float MaximumWidthFraction = 0.4f;
    /// <summary>Gets the maximum height as a fraction of the game viewport.</summary>
    public const float MaximumHeightFraction = 0.95f;

    /// <summary>Gets the grid width in design units.</summary>
    public static int GridWidth => Columns * CellPixels + (Columns - 1) * CellSpacing;

    /// <summary>Gets the toolbar height in design units.</summary>
    public static int ToolbarHeight => ToolbarButtons * ToolbarButton + (ToolbarButtons - 1) * ToolbarSpacing;

    /// <summary>Gets the grid height in design units for a row count.</summary>
    public static int GetGridHeight(int rows) => rows * CellPixels + (rows - 1) * CellSpacing;

    /// <summary>Gets the outer height for a row count; the toolbar sets a floor for very short grids.</summary>
    public static int GetHeight(int rows) => Chrome + Math.Max(GetGridHeight(rows), ToolbarHeight);

    /// <summary>
    /// Counts the rows that fit the viewport at native scale, then <c>max(2, possibleRows * size / 4)</c>.
    /// </summary>
    public static int GetRows(TerminalSize size, float viewportHeight, float canvasScale)
    {
        if (!IsPositiveFinite(viewportHeight) || !IsPositiveFinite(canvasScale)) return Rows;
        var designHeight = viewportHeight / canvasScale / NativeScale;
        var available = designHeight - 2 * ViewportMargin - Chrome;
        var possibleRows = Math.Max(0, (int)((available + CellSpacing) / (CellPixels + CellSpacing)));
        var quarter = Math.Max(1, Math.Min(4, (int)size));
        return Math.Max(MinimumRows, possibleRows * quarter / 4);
    }

    /// <summary>Fits the default-height window without changing any global UI scaling.</summary>
    public static float GetScale(float viewportWidth, float viewportHeight, float canvasScale) =>
        GetScale(viewportWidth, viewportHeight, canvasScale, Height);

    /// <summary>Fits a window of the given design height; only windows larger than the viewport shrink.</summary>
    public static float GetScale(float viewportWidth, float viewportHeight, float canvasScale, int height)
    {
        if (!IsPositiveFinite(viewportWidth) || !IsPositiveFinite(viewportHeight) ||
            !IsPositiveFinite(canvasScale) || height <= 0)
        {
            return NativeScale;
        }

        var widthScale = viewportWidth / Width / canvasScale * MaximumWidthFraction;
        var heightScale = viewportHeight / height / canvasScale * MaximumHeightFraction;
        return Math.Min(NativeScale, Math.Min(widthScale, heightScale));
    }

    /// <summary>Clamps a centered window offset so the full window remains inside its viewport.</summary>
    public static float GetClampedOffset(float offset, float viewportLength, float windowLength, float margin = 0)
    {
        if (!IsPositiveFinite(viewportLength) || !IsPositiveFinite(windowLength) ||
            float.IsNaN(offset) || float.IsInfinity(offset))
        {
            return 0;
        }

        var padding = IsPositiveFinite(margin) ? margin : 0;
        var limit = Math.Max(0, (viewportLength - windowLength) / 2 - padding);
        return Math.Max(-limit, Math.Min(limit, offset));
    }

    private static bool IsPositiveFinite(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
}
