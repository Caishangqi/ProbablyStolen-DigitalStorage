using System;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Routes owned control hints through the lifecycle scoped tooltip service.</summary>
internal static class TerminalTooltipPresenter
{
    private static ITooltipService? _service;

    internal static ITooltipService? Service => _service;

    internal static bool ConsumeScroll(bool altHeld, float wheelDelta, int frameId) =>
        _service?.TryConsumeScroll(altHeld, wheelDelta, frameId) == true;

    internal static void SetService(ITooltipService? service)
    {
        _service = service;
    }

    internal static void SetText(CustomUIElement? element, string text)
    {
        if (element?.root == null) return;
        if (_service != null)
        {
            if (string.IsNullOrEmpty(text) && _service is NativeTooltipService native)
                native.ClearTooltip(element);
            else
                _service.SetTooltip(element, text ?? string.Empty, element);
            return;
        }

        var trigger = element.root.GetComponent<CustomUITooltipTrigger>();
        if (string.IsNullOrEmpty(text))
        {
            if (trigger != null) CustomUIManager.Instance?.HideTooltip(trigger);
            foreach (var owned in element.root.GetComponents<DigitalStorageTooltipTrigger>())
                UnityEngine.Object.Destroy(owned);
            return;
        }
        if (trigger != null && !string.Equals(trigger.tooltipText, text, StringComparison.Ordinal))
            CustomUIManager.Instance?.HideTooltip(trigger);
        element.SetTooltip(text ?? string.Empty);
    }
}
