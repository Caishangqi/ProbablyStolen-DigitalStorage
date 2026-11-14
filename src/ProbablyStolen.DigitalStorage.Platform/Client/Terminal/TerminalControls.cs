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
}
