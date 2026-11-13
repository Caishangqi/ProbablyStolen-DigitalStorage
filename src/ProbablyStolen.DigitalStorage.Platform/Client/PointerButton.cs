using System;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal static class PointerButton
{
    /// <summary>
    /// Adds a button that only the mouse can press. A clicked button never takes keyboard focus, so Space (also the
    /// advance-dialogue key) and the arrow keys cannot press it again or move to a neighbouring button.
    /// </summary>
    internal static CustomUIBuilder AddPointerButton(this CustomUIBuilder builder, string text, Action onClick,
        string? id = null)
    {
        builder.AddButton(text, onClick, id);
        if (builder.Last.button != null)
            builder.Last.button.navigation = new Navigation { mode = Navigation.Mode.None };
        return builder;
    }
}
