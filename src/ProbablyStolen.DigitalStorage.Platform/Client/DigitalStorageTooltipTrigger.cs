using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Tooltip trigger owned by Digital Storage so replacement handles cannot destroy another generation.</summary>
internal sealed class DigitalStorageTooltipTrigger : CustomUITooltipTrigger
{
    internal void SetText(string text) => tooltipText = text ?? string.Empty;

    private void OnDisable() => CustomUIManager.Instance?.HideTooltip(this);
}
