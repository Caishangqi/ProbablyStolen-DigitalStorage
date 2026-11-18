using System;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;

/// <summary>
/// The back button of a sub-screen: a square button with a left arrow.
/// </summary>
internal static class TabButton
{
    private const float Size = 32f;

    internal static CustomUIElement Add(CustomUIBuilder builder, string tooltip, Action onClick)
    {
        builder.AddPointerButton(string.Empty, onClick, null);
        var button = builder.Last;
        TerminalControls.SetSize(button, Size, Size);
        var arrow = PixelArrow.Add(button.root.transform, PixelArrow.Direction.Left, MachineryStyle.BUTTON_TEXT);
        arrow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        TerminalTooltipPresenter.SetButtonText(button, tooltip);
        return button;
    }
}
