using System;
using ProbablyStolen.DigitalStorage.Api.Client;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Defines the terminal's design coordinates, row count, slot size fit and viewport fit.</summary>
public static class TerminalWindowMetrics
{
    /// <summary>Gets the fixed number of columns.</summary>
    public const int Columns = 9;
    /// <summary>Gets the row count used before a viewport is known.</summary>
    public const int Rows = 4;
    /// <summary>Gets the minimum visible rows.</summary>
    public const int MinimumRows = 2;
    /// <summary>Gets the gap between slots in design units.</summary>
    public const int CellSpacing = SlotMetrics.Spacing;
    /// <summary>Gets the gap between dock buttons: the grid's slot gap.</summary>
    public const int DockSpacing = CellSpacing;
    /// <summary>Gets the inner padding of the function rail and of selection panels.</summary>
    public const int DockPadding = 6;
    /// <summary>Gets the gap between the rail and the terminal frame, and between a panel and its button.</summary>
    public const int DockGap = 4;
    /// <summary>Gets the scrollbar track width in design units.</summary>
    public const int ScrollbarWidth = 12;
    /// <summary>Gets the fixed scrollbar handle height in design units.</summary>
    public const int ScrollbarHandle = 30;
    /// <summary>
    /// Gets the horizontal window padding in design units: the machinery frame's content padding (16) on each side
    /// (<c>CustomUIMachineryStyle.CreateFrame</c>, <c>ContentPad</c>), so the left and right margins match.
    /// </summary>
    public const int HorizontalChrome = 2 * 16;
    /// <summary>Gets the gap between the grid and the scrollbar in design units.</summary>
    public const int ScrollbarGap = 4;
    /// <summary>Gets the status row font size at the default slot size.</summary>
    public const int BaseStatusFontSize = 14;
    // Text rows grow by a smaller step than the slots, so large slots stay in proportion with the game's own text.
    private static readonly int[] SearchHeights = { 32, 34, 36, 40 };
    private static readonly int[] StatusHeights = { 20, 21, 22, 24 };
    /// <summary>Gets the free space kept above and below the window when counting rows, in design units.</summary>
    public const int ViewportMargin = 40;
    /// <summary>Gets the correction from doubled machinery controls to native window coordinates.</summary>
    public const float NativeScale = 0.5f;
    /// <summary>Gets the maximum width as a fraction of the game viewport.</summary>
    public const float MaximumWidthFraction = 0.4f;
    /// <summary>Gets the maximum height as a fraction of the game viewport.</summary>
    public const float MaximumHeightFraction = 0.95f;

    /// <summary>Gets the grid width in design units.</summary>
    public static int GetGridWidth(SlotMetrics slots) => Columns * Slot(slots) + (Columns - 1) * CellSpacing;

    /// <summary>Gets the grid height in design units for a row count.</summary>
    public static int GetGridHeight(SlotMetrics slots, int rows) => rows * Slot(slots) + (rows - 1) * CellSpacing;

    /// <summary>
    /// Gets the outer width in design units: grid, gap and scrollbar plus window padding. The docks sit outside the
    /// frame, so they take no width here.
    /// </summary>
    public static int GetWidth(SlotMetrics slots) => HorizontalChrome + GetGridWidth(slots) + ScrollbarGap + ScrollbarWidth;

    /// <summary>Gets the outer height for a row count; the docks are outside the frame and set no floor.</summary>
    public static int GetHeight(SlotMetrics slots, int rows) => GetChrome(slots) + GetGridHeight(slots, rows);

    /// <summary>
    /// Gets the outer height that is not grid: frame title 40 and rule 2, content padding 16 above and below, the search
    /// row, the status row and the two 6-unit gaps between the rows.
    /// </summary>
    public static int GetChrome(SlotMetrics slots) => 40 + 2 + 16 + 16 + GetSearchHeight(slots) + 6 + 6 + GetStatusHeight(slots);

    /// <summary>Gets the search row height for the slot size in use.</summary>
    public static int GetSearchHeight(SlotMetrics slots) => SearchHeights[TextStep(slots)];

    /// <summary>Gets the status row height for the slot size in use.</summary>
    public static int GetStatusHeight(SlotMetrics slots) => StatusHeights[TextStep(slots)];

    /// <summary>Gets the status row font size for the slot size in use.</summary>
    public static int GetStatusFontSize(SlotMetrics slots) => BaseStatusFontSize + TextStep(slots);

    /// <summary>
    /// Gets how many units the search and status text grow over their original size: one per slot size step.
    /// </summary>
    public static int TextStep(SlotMetrics slots) =>
        (int)(slots ?? throw new ArgumentNullException(nameof(slots))).EffectiveTier;

    /// <summary>Gets the dock button side, set by its own size choice.</summary>
    public static int GetDockButton(SlotMetrics slots) =>
        (slots ?? throw new ArgumentNullException(nameof(slots))).DockButtonPixels;

    /// <summary>Gets the dock width: one button plus padding on both sides.</summary>
    public static int GetDockWidth(SlotMetrics slots) => GetDockButton(slots) + 2 * DockPadding;

    /// <summary>Gets the icon side inside a dock button.</summary>
    public static int GetDockIcon(SlotMetrics slots) =>
        (slots ?? throw new ArgumentNullException(nameof(slots))).DockIconPixels;

    /// <summary>
    /// Counts the rows that fit the viewport at native scale, then <c>max(2, possibleRows * size / 4)</c>.
    /// </summary>
    public static int GetRows(TerminalSize size, SlotMetrics slots, float viewportHeight, float canvasScale)
    {
        var slot = Slot(slots);
        if (!IsPositiveFinite(viewportHeight) || !IsPositiveFinite(canvasScale)) return Rows;
        var designHeight = viewportHeight / canvasScale / NativeScale;
        var available = designHeight - 2 * ViewportMargin - GetChrome(slots);
        var possibleRows = Math.Max(0, (int)((available + CellSpacing) / (slot + CellSpacing)));
        var quarter = Math.Max(1, Math.Min(4, (int)size));
        return Math.Max(MinimumRows, possibleRows * quarter / 4);
    }

    /// <summary>
    /// Gets the largest size up to <paramref name="requested"/> whose window fits the viewport at native scale with the
    /// minimum rows. <see cref="SizeTier.Default"/> is the floor; when even that does not fit, the window shrinks
    /// through <see cref="GetScale(float, float, float, int, int)"/>. An unknown viewport keeps the request.
    /// </summary>
    public static SizeTier ResolveTier(SizeTier requested, float viewportWidth, float viewportHeight,
        float canvasScale)
    {
        if (!Enum.IsDefined(typeof(SizeTier), requested)) throw new ArgumentOutOfRangeException(nameof(requested));
        for (var tier = requested; tier > SizeTier.Default; tier--)
        {
            var slots = SlotMetrics.Create(tier, tier, IconMarginTier.Standard, SizeTier.Default);
            if (GetScale(viewportWidth, viewportHeight, canvasScale, GetWidth(slots), GetHeight(slots, MinimumRows)) >=
                NativeScale)
                return tier;
        }

        return SizeTier.Default;
    }

    /// <summary>Gets the measurements of a requested slot size, margin and dock button size on the given viewport.</summary>
    public static SlotMetrics Resolve(SizeTier requested, IconMarginTier margin, SizeTier dock, float viewportWidth,
        float viewportHeight, float canvasScale) =>
        SlotMetrics.Create(requested, ResolveTier(requested, viewportWidth, viewportHeight, canvasScale), margin, dock);

    /// <summary>Fits a window of the given design width and height; only windows larger than the viewport shrink.</summary>
    public static float GetScale(float viewportWidth, float viewportHeight, float canvasScale, int width, int height)
    {
        if (!IsPositiveFinite(viewportWidth) || !IsPositiveFinite(viewportHeight) ||
            !IsPositiveFinite(canvasScale) || width <= 0 || height <= 0)
        {
            return NativeScale;
        }

        var widthScale = viewportWidth / width / canvasScale * MaximumWidthFraction;
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

    private static int Slot(SlotMetrics slots) => (slots ?? throw new ArgumentNullException(nameof(slots))).SlotPixels;

    private static bool IsPositiveFinite(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
}
