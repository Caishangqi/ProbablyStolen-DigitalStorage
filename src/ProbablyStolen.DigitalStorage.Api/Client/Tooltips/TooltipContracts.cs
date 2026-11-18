using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Api.Client.Tooltips;

/// <summary>Identifies the presentation used by a tooltip panel.</summary>
public enum TooltipStyleKind
{
    /// <summary>Uses the game's native tooltip frame.</summary>
    Native,
    /// <summary>Uses the supplied RGB frame and colors.</summary>
    Rgb
}

/// <summary>Immutable visual settings for one tooltip panel.</summary>
public sealed class TooltipStyle
{
    /// <summary>Gets the native game frame and text presentation.</summary>
    public static TooltipStyle Native { get; } = new TooltipStyle(TooltipStyleKind.Native, null, null, null);

    /// <summary>Creates an RGB framed style. Colors use the 24 bit RGB form <c>0xRRGGBB</c>.</summary>
    public TooltipStyle(TooltipStyleKind kind, int? borderRgb, int? backgroundRgb, int? textRgb)
    {
        if (!Enum.IsDefined(typeof(TooltipStyleKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Validate(borderRgb, nameof(borderRgb));
        Validate(backgroundRgb, nameof(backgroundRgb));
        Validate(textRgb, nameof(textRgb));
        Kind = kind;
        BorderRgb = borderRgb;
        BackgroundRgb = backgroundRgb;
        TextRgb = textRgb;
    }

    /// <summary>Gets the presentation kind.</summary>
    public TooltipStyleKind Kind { get; }
    /// <summary>Gets the optional border color.</summary>
    public int? BorderRgb { get; }
    /// <summary>Gets the optional background color.</summary>
    public int? BackgroundRgb { get; }
    /// <summary>Gets the optional default plain text color.</summary>
    public int? TextRgb { get; }

    private static void Validate(int? value, string name)
    {
        if (value.HasValue && (value.Value < 0 || value.Value > 0xFFFFFF))
            throw new ArgumentOutOfRangeException(name, "RGB colors must be between 0x000000 and 0xFFFFFF.");
    }
}

/// <summary>Public tooltip service for one Digital Storage mod lifecycle.</summary>
public interface ITooltipService
{
    /// <summary>Creates an owned tooltip group. The owner is used for lifecycle cleanup.</summary>
    ITooltip CreateTooltip(object owner);

    /// <summary>Replaces the native tooltip binding on one of this mod's controls.</summary>
    IDisposable SetTooltip(CustomUIElement element, string text, object owner);

    /// <summary>Registers an obstacle provider with a stable identifier.</summary>
    IDisposable RegisterObstacle(string id, Func<IReadOnlyList<RectTransform>> provider);

    /// <summary>Consumes one frame's Alt wheel input when the visible tooltip can scroll.</summary>
    bool TryConsumeScroll(bool altHeld, float wheelDelta, int frameId);
}

/// <summary>A group of panels positioned and scrolled together around one source.</summary>
public interface ITooltip : IDisposable
{
    /// <summary>Adds or returns a panel with a stable id and ordering value.</summary>
    ITooltipPanel AddPanel(string id, int order);

    /// <summary>Shows the group around a screen position.</summary>
    void Show(Vector2 screenPosition);

    /// <summary>Shows the group around a UI anchor.</summary>
    void Show(RectTransform anchor);

    /// <summary>Hides the group while retaining its panel content.</summary>
    void Hide();
}

/// <summary>One independently styled panel in a tooltip group.</summary>
public interface ITooltipPanel
{
    /// <summary>Sets plain text. The service reflows it when its width changes.</summary>
    void SetText(string text);

    /// <summary>Sets native rich text. The caller resubmits after changing the builder.</summary>
    void SetRichText(RichTextBuilder builder);

    /// <summary>Sets the frame, background and default plain text style.</summary>
    void SetStyle(TooltipStyle style);
}

/// <summary>Versioned key used by <c>ModHelper.Expose</c> and <c>ModHelper.Lookup</c>.</summary>
public static class TooltipServiceKeys
{
    /// <summary>Provider mod id.</summary>
    public const string ModId = "digital_storage";
    /// <summary>Tooltip service key.</summary>
    public const string ServiceKey = "tooltip.v1";
}
