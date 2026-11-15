using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>
/// A box shown next to the native tooltip. Features register their own panels with their colors, so the storage view
/// lays out and draws panels without knowing any feature.
/// </summary>
public sealed class TooltipPanelDefinition
{
    /// <summary>The id of the storage view's own panel, which also carries the usage hints.</summary>
    public const string HintId = "hint";

    /// <summary>Creates a panel definition; colors are 0xRRGGBB. Without a border color the game's own border is used.</summary>
    public TooltipPanelDefinition(string id, int order, int titleRgb, int labelRgb, int bodyRgb, int? borderRgb = null)
    {
        Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("A panel id is required.", nameof(id)) : id;
        Order = order;
        TitleRgb = Rgb(titleRgb, nameof(titleRgb));
        LabelRgb = Rgb(labelRgb, nameof(labelRgb));
        BodyRgb = Rgb(bodyRgb, nameof(bodyRgb));
        BorderRgb = borderRgb.HasValue ? Rgb(borderRgb.Value, nameof(borderRgb)) : null;
    }

    /// <summary>
    /// Gets the storage view's own panel, colored like the game's tooltip hints: white title, light blue captions and
    /// light turquoise content.
    /// </summary>
    public static TooltipPanelDefinition Hint { get; } = new(HintId, 0, 0xC4C4B0, 0x429CBD, 0x42A28B);

    /// <summary>Gets the unique id lines refer to.</summary>
    public string Id { get; }

    /// <summary>Gets the order; lower panels sit closer to the native tooltip.</summary>
    public int Order { get; }

    /// <summary>Gets the title color.</summary>
    public int TitleRgb { get; }

    /// <summary>Gets the caption color.</summary>
    public int LabelRgb { get; }

    /// <summary>Gets the content color.</summary>
    public int BodyRgb { get; }

    /// <summary>Gets the border tint, or null for the game's own border.</summary>
    public int? BorderRgb { get; }

    /// <summary>Gets the color of a line role.</summary>
    public int ColorOf(TooltipRole role) => role switch
    {
        TooltipRole.Title => TitleRgb,
        TooltipRole.Label => LabelRgb,
        _ => BodyRgb
    };

    private static int Rgb(int value, string name) =>
        value is >= 0 and <= 0xFFFFFF ? value : throw new ArgumentOutOfRangeException(name);
}

/// <summary>One panel to show for a slot: its definition and the lines extensions added, in order.</summary>
public sealed class TooltipPanel
{
    /// <summary>Creates a panel.</summary>
    public TooltipPanel(TooltipPanelDefinition definition, IReadOnlyList<TooltipLine> lines)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
    }

    /// <summary>Gets the definition.</summary>
    public TooltipPanelDefinition Definition { get; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<TooltipLine> Lines { get; }
}
