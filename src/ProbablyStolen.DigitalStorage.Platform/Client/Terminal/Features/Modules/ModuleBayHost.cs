using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Modules;

/// <summary>Finds and hosts a terminal's native module panel in the terminal's custom window.</summary>
internal sealed class ModuleBayHost
{
    private const string ModulesPanelRole = "module";
    private readonly MachineryPanelHost _host;

    internal ModuleBayHost(Action<string> diagnostic) =>
        _host = new MachineryPanelHost(diagnostic, "Digital Storage Terminal Module Bay Host",
            "module_bay_embed", "module_bay_restore");

    internal bool IsShowing => _host.IsShowing;

    internal static bool TryFind(GameItem terminal, out GameGridInventory bay)
    {
        bay = null!;
        if (!TryFindPanel(terminal, out var panel, out _) || panel.content is not MachineryIntake intake ||
            intake.content is not GameGridInventory grid)
            return false;
        bay = grid;
        return true;
    }

    internal static bool TryFindPanel(GameItem terminal, out MachineryPanel panel, out MachineryPanelRow row)
    {
        panel = null!;
        row = null!;
        if (terminal.contentWindow?.childElement is not MachineryPanelRow found ||
            found.GetPanel("module") is not { } modules)
            return false;
        panel = modules;
        row = found;
        return true;
    }

    internal bool Show(MachineryPanel panel, MachineryPanelRow home, RectTransform container) =>
        _host.Show(panel, home, container, 1f / TerminalWindowMetrics.NativeScale);

    internal void Restore(string reason) => _host.Restore(reason);

    internal void Dispose(string reason) => _host.Dispose(reason);
}
