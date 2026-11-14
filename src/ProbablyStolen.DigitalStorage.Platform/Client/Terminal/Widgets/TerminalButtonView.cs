using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using TMPro;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;

/// <summary>Draws one <see cref="ButtonState"/> as a square text button, one storage cell in size.</summary>
internal static class TerminalButtonView
{
    internal static CustomUIElement Add(CustomUIBuilder builder, ModText text, ButtonState state, Action onClick)
    {
        builder.AddPointerButton(text.Get(state.Appearance.LabelKey), onClick, null);
        var button = builder.Last;
        TerminalControls.SetSize(button, TerminalWindowMetrics.DockButton, TerminalWindowMetrics.DockButton);
        if (button.text != null)
        {
            // Labels are short ("名", "模"), but a translation may be longer; shrink rather than overflow the cell.
            button.text.alignment = TextAlignmentOptions.Center;
            button.text.enableAutoSizing = true;
            button.text.fontSizeMin = 8f;
            button.text.fontSizeMax = 14f;
            button.text.overflowMode = TextOverflowModes.Ellipsis;
            if (state.Selected) button.text.color = MachineryStyle.VALUE_TEXT;
        }

        var appearance = state.Appearance;
        TerminalTooltipPresenter.SetText(button, appearance.TooltipArgKey == null
            ? text.Get(appearance.TooltipKey)
            : text.Get(appearance.TooltipKey, text.Get(appearance.TooltipArgKey)));
        return button;
    }
}
