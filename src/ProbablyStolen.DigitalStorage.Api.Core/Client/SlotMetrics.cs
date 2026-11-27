using System;

namespace ProbablyStolen.DigitalStorage.Api.Client;

/// <summary>A size a player chose for item slots or for dock buttons; each step adds 8 design units.</summary>
public enum SizeTier
{
    /// <summary>40 design units, the original size.</summary>
    Default = 0,

    /// <summary>48 design units.</summary>
    Small = 1,

    /// <summary>56 design units.</summary>
    Medium = 2,

    /// <summary>64 design units.</summary>
    Large = 3
}

/// <summary>How far an item picture sits inside its slot button.</summary>
public enum IconMarginTier
{
    /// <summary>3 design units at the default slot size.</summary>
    Compact = 0,

    /// <summary>6 design units at the default slot size, the original margin.</summary>
    Standard = 1,

    /// <summary>9 design units at the default slot size.</summary>
    Loose = 2
}

/// <summary>
/// The measurements, in design units, of an item slot of the storage terminal and the partition editor, and of the dock
/// buttons of every terminal-style window. Slot lengths except the gap and border scale with the slot size; the dock
/// buttons and their icons follow their own size. Values are rounded to the nearest unit.
/// </summary>
public sealed class SlotMetrics : IEquatable<SlotMetrics>
{
    /// <summary>The slot side at <see cref="SizeTier.Default"/>; every scaled length is given at this size.</summary>
    public const int BaseSlotPixels = 40;

    /// <summary>The gap between slots, the same at every size.</summary>
    public const int Spacing = 3;

    /// <summary>The border between a slot's panel and its button, the same at every size.</summary>
    public const int BorderPadding = 2;

    private const int SizeStep = 8;
    private const int MarginStep = 3;
    private const int BaseDockIconPixels = 32;
    private const int BaseBadgeFontSize = 12;
    private const int BaseBadgeOffsetX = 3;
    private const int BaseBadgeOffsetY = 2;

    private SlotMetrics(SizeTier requested, SizeTier effective, IconMarginTier margin, SizeTier dock)
    {
        RequestedTier = requested;
        EffectiveTier = effective;
        MarginTier = margin;
        DockTier = dock;
        SlotPixels = Pixels(effective);
        DockButtonPixels = Pixels(dock);
        DockIconPixels = Round(BaseDockIconPixels * (double)DockButtonPixels / BaseSlotPixels);
        ButtonPixels = SlotPixels - 2 * BorderPadding;
        IconInset = Scaled(MarginStep * ((int)margin + 1));
        IconPixels = ButtonPixels - 2 * IconInset;
        BadgeFontSize = Scaled(BaseBadgeFontSize);
        BadgeOffsetX = Scaled(BaseBadgeOffsetX);
        BadgeOffsetY = Scaled(BaseBadgeOffsetY);
    }

    /// <summary>Gets the measurements of the original slot: default size, standard margin.</summary>
    public static SlotMetrics Default { get; } =
        new(SizeTier.Default, SizeTier.Default, IconMarginTier.Standard, SizeTier.Default);

    /// <summary>Gets the size the player chose.</summary>
    public SizeTier RequestedTier { get; }

    /// <summary>
    /// Gets the size in use: the chosen one, or a smaller one when a window of the chosen size would not fit the screen.
    /// </summary>
    public SizeTier EffectiveTier { get; }

    /// <summary>Gets whether a smaller size than the chosen one is in use.</summary>
    public bool IsReduced => EffectiveTier != RequestedTier;

    /// <summary>Gets the icon margin the player chose.</summary>
    public IconMarginTier MarginTier { get; }

    /// <summary>Gets the dock button size the player chose. Docks sit outside the window frame, so it is never reduced.</summary>
    public SizeTier DockTier { get; }

    /// <summary>Gets the side of a dock button.</summary>
    public int DockButtonPixels { get; }

    /// <summary>Gets the side of the icon inside a dock button.</summary>
    public int DockIconPixels { get; }

    /// <summary>Gets the slot's scale against <see cref="BaseSlotPixels"/>.</summary>
    public float Scale => SlotPixels / (float)BaseSlotPixels;

    /// <summary>Gets the side of a slot's square panel.</summary>
    public int SlotPixels { get; }

    /// <summary>Gets the side of the button inside the border.</summary>
    public int ButtonPixels { get; }

    /// <summary>Gets the distance from the button edge to the item picture.</summary>
    public int IconInset { get; }

    /// <summary>Gets the side of the item picture area.</summary>
    public int IconPixels { get; }

    /// <summary>Gets the font size of the quantity badge.</summary>
    public int BadgeFontSize { get; }

    /// <summary>Gets the horizontal distance from the slot edge to the quantity badge.</summary>
    public int BadgeOffsetX { get; }

    /// <summary>Gets the vertical distance from the slot edge to the quantity badge.</summary>
    public int BadgeOffsetY { get; }

    /// <summary>Creates the measurements of a slot size the screen can hold, an icon margin and a dock button size.</summary>
    /// <param name="requested">The slot size the player chose.</param>
    /// <param name="effective">The slot size in use; not larger than <paramref name="requested"/>.</param>
    /// <param name="margin">The icon margin.</param>
    /// <param name="dock">The dock button size.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is undefined, or the size in use exceeds the chosen one.</exception>
    public static SlotMetrics Create(SizeTier requested, SizeTier effective, IconMarginTier margin, SizeTier dock)
    {
        if (!Enum.IsDefined(typeof(SizeTier), requested)) throw new ArgumentOutOfRangeException(nameof(requested));
        if (!Enum.IsDefined(typeof(SizeTier), effective) || effective > requested)
            throw new ArgumentOutOfRangeException(nameof(effective));
        if (!Enum.IsDefined(typeof(IconMarginTier), margin)) throw new ArgumentOutOfRangeException(nameof(margin));
        if (!Enum.IsDefined(typeof(SizeTier), dock)) throw new ArgumentOutOfRangeException(nameof(dock));
        return new SlotMetrics(requested, effective, margin, dock);
    }

    /// <summary>Scales a length given at <see cref="BaseSlotPixels"/> to this slot size.</summary>
    public int Scaled(int baseUnits) => Round(baseUnits * (double)SlotPixels / BaseSlotPixels);

    /// <inheritdoc />
    public bool Equals(SlotMetrics? other) => other is not null && RequestedTier == other.RequestedTier &&
        EffectiveTier == other.EffectiveTier && MarginTier == other.MarginTier && DockTier == other.DockTier;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SlotMetrics);

    /// <inheritdoc />
    public override int GetHashCode() =>
        (((int)RequestedTier * 4 + (int)EffectiveTier) * 3 + (int)MarginTier) * 4 + (int)DockTier;

    /// <inheritdoc />
    public override string ToString() =>
        $"SlotMetrics(requested={RequestedTier}, effective={EffectiveTier}, margin={MarginTier}, dock={DockTier}, slot={SlotPixels})";

    private static int Pixels(SizeTier tier) => BaseSlotPixels + SizeStep * (int)tier;

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>Carries the slot measurements before and after a change.</summary>
public sealed class SlotMetricsChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments of one change.</summary>
    public SlotMetricsChangedEventArgs(SlotMetrics previous, SlotMetrics current)
    {
        Previous = previous ?? throw new ArgumentNullException(nameof(previous));
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    /// <summary>Gets the measurements before the change.</summary>
    public SlotMetrics Previous { get; }

    /// <summary>Gets the measurements after the change.</summary>
    public SlotMetrics Current { get; }
}

/// <summary>
/// Reads the slot measurements of the storage terminal and the partition editor, for panels that line up with their
/// slots. Only the player changes them. Read and subscribe on the Unity main thread; <see cref="Changed"/> is raised there.
/// </summary>
public interface ISlotMetricsProvider
{
    /// <summary>
    /// Gets the measurements in use. They are read from the player's settings when a terminal-style window first opens
    /// or the settings are applied; until then this is <see cref="SlotMetrics.Default"/>, and <see cref="Changed"/>
    /// reports the update.
    /// </summary>
    SlotMetrics Current { get; }

    /// <summary>Raised after a chosen size or margin, or the slot size the screen can hold, changes.</summary>
    event EventHandler<SlotMetricsChangedEventArgs>? Changed;
}
