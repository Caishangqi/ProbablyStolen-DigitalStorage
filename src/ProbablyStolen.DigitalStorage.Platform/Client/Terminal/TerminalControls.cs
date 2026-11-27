using TMPro;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Layout helpers shared by the terminal window and its views.</summary>
internal static class TerminalControls
{
    /// <summary>Fixes a builder control to one size; the builder's layout groups otherwise size it to its content.</summary>
    internal static void SetSize(CustomUIElement control, float width, float height = 32f)
    {
        var layout = control.root.GetComponent<LayoutElement>() ?? control.root.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = width;
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleWidth = layout.flexibleHeight = 0f;
    }

    /// <summary>
    /// Enlarges a label's or an input's text, placeholder included, by <paramref name="step"/> units over the size its
    /// native prefab gave it. Call once, right after the control is created.
    /// </summary>
    internal static void GrowText(CustomUIElement control, int step)
    {
        if (step == 0) return;
        if (control.text != null) control.text.fontSize += step;
        if (control.input == null) return;
        if (control.input.textComponent != null) control.input.textComponent.fontSize += step;
        if (control.input.placeholder is TMP_Text placeholder) placeholder.fontSize += step;
    }
}
