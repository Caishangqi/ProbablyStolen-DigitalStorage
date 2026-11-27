using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.DisplaySettings;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using TMPro;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.DisplaySettings;

/// <summary>
/// The slot size, icon margin and dock button size page. A choice is saved at once; the window then rebuilds at the new
/// size and shows this page again, so the player sees the result without leaving it.
/// </summary>
internal sealed class DisplaySettingsScreen : SubScreen
{
    private const float Gap = 6f;
    private const float ButtonHeight = 32f;

    internal DisplaySettingsScreen(TerminalWindowHost window) : base(window)
    {
    }

    /// <inheritdoc />
    public override string ScreenId => DisplaySettingsFeature.ScreenId;

    internal override bool FitsContent => true;

    internal override SubScreen Reopen() => new DisplaySettingsScreen(Window);

    protected override bool BuildContent(CustomUIBuilder builder)
    {
        var text = Window.Text;
        var slots = Window.SlotMetrics;
        var width = TerminalWindowMetrics.GetGridWidth(slots);
        var service = Window.SlotMetricsService;
        AddHeading(builder, text.Get("display_settings_slot_size"), width);
        AddChoices(builder, DisplaySettingsFeature.Sizes, slots.RequestedTier, width,
            tier => text.Get(DisplaySettingsFeature.SizeLabelKey(tier)), service.SetSlotSize);
        if (slots.IsReduced)
        {
            builder.AddLabel(text.Get("display_settings_reduced",
                text.Get(DisplaySettingsFeature.SizeLabelKey(slots.EffectiveTier))));
            Wrap(builder.Last, width);
        }

        AddHeading(builder, text.Get("display_settings_icon_margin"), width);
        AddChoices(builder, DisplaySettingsFeature.IconMargins, slots.MarginTier, width,
            margin => text.Get(DisplaySettingsFeature.IconMarginLabelKey(margin)), service.SetIconMargin);
        AddHeading(builder, text.Get("display_settings_dock_button_size"), width);
        AddChoices(builder, DisplaySettingsFeature.Sizes, slots.DockTier, width,
            tier => text.Get(DisplaySettingsFeature.SizeLabelKey(tier)), service.SetDockButtonSize);
        builder.AddLabel(text.Get("display_settings_note"));
        Wrap(builder.Last, width);
        return true;
    }

    private static void AddHeading(CustomUIBuilder builder, string label, float width)
    {
        builder.AddHeader(label);
        TerminalControls.SetSize(builder.Last, width, 20f);
    }

    private static void AddChoices<T>(CustomUIBuilder builder, IReadOnlyList<T> choices, T selected, float width,
        Func<T, string> label, Action<T> choose) where T : struct, Enum
    {
        var buttonWidth = (width - Gap * (choices.Count - 1)) / choices.Count;
        builder.BeginRow(Gap);
        foreach (var choice in choices)
        {
            var value = choice;
            builder.AddPointerButton(label(value), () => choose(value));
            TerminalControls.SetSize(builder.Last, buttonWidth, ButtonHeight);
            StyleAsTab(builder.Last, EqualityComparer<T>.Default.Equals(value, selected));
        }

        builder.End();
    }

    /// <summary>
    /// Draws a choice the way the game draws its tabs (<c>MachineryTabs.RefreshTabs</c>): a tab plate that is brighter
    /// and brighter-labelled when selected, with no press or hover change.
    /// </summary>
    private static void StyleAsTab(CustomUIElement choice, bool selected)
    {
        if (choice.image != null)
        {
            var plate = MachineryStyle.TAB_PLATE;
            plate.a = selected ? MachineryStyle.TAB_PLATE_ALPHA_ACTIVE : MachineryStyle.TAB_PLATE_ALPHA;
            choice.image.sprite = MachineryStyle.TabPlate;
            choice.image.type = Image.Type.Sliced;
            choice.image.color = plate;
        }

        if (choice.button != null) choice.button.transition = Selectable.Transition.None;
        if (choice.text != null) choice.text.color = selected ? MachineryStyle.TAB_TEXT_ACTIVE : MachineryStyle.TAB_TEXT;
    }

    private static void Wrap(CustomUIElement label, float width)
    {
        if (label.text != null)
        {
            label.text.fontSize = 14f;
            label.text.enableWordWrapping = true;
            label.text.overflowMode = TextOverflowModes.Overflow;
        }

        TerminalControls.SetSize(label, width, -1f);
    }
}
