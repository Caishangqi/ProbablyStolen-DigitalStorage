using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Modules;

/// <summary>
/// The modules page. It shows the terminal's whole native module panel, title and centred bay, drawn here by
/// <see cref="ModuleBayHost"/>; the panel stays in the machinery row's list, so saving and module effects are
/// unchanged.
/// </summary>
internal sealed class TerminalModulesScreen : SubScreen
{
    private ModuleBayHost? _bayHost;

    internal TerminalModulesScreen(TerminalWindow window) : base(window)
    {
    }

    /// <inheritdoc />
    internal override string UnavailableKey => "terminal_modules_unavailable";

    /// <inheritdoc />
    protected override bool BuildContent(CustomUIBuilder builder)
    {
        if (!ModuleBayHost.TryFindPanel(Window.Terminal, out var panel, out var row))
        {
            Window.WriteDiagnostic("module_bay_embed result=refused reason=panel_not_found");
            return false;
        }

        var holder = new GameObject("module_bay", typeof(RectTransform)).GetComponent<RectTransform>();
        holder.SetParent(builder.CurrentContainer, false);
        var scale = 1f / TerminalWindowMetrics.NativeScale;
        var layout = holder.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = panel.widthPixels * scale;
        layout.minHeight = layout.preferredHeight = panel.heightPixels * scale;
        _bayHost = new ModuleBayHost(Window.WriteDiagnostic);
        return _bayHost.Show(panel, row, holder);
    }

    /// <inheritdoc />
    internal override void Dispose()
    {
        // Before the controls go: the panel must leave this page while its node still exists.
        _bayHost?.Dispose("modules_screen_closed");
        _bayHost = null;
        base.Dispose();
    }
}
