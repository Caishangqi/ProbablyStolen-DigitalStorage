using System;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Invalidates visible control hints when live terminal content changes.</summary>
internal static class TerminalTooltipPresenter
{
    internal static void SetText(CustomUIElement? element, string text)
    {
        if (element?.root == null) return;
        var trigger = element.root.GetComponent<CustomUITooltipTrigger>();
        if (trigger != null && !string.Equals(trigger.tooltipText, text, StringComparison.Ordinal))
        {
            // SetTooltip changes only the next pointer-enter text in the target game build.
            CustomUIManager.Instance?.HideTooltip(trigger);
        }
        element.SetTooltip(text);
    }
}
