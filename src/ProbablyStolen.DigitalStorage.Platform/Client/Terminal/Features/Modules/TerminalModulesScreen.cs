using System;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Modules;

/// <summary>Displays the borrowed native module panel and its registered stage.</summary>
internal sealed class TerminalModulesScreen : SubScreen
{
    private ModuleBayHost? _bayHost;
    private IDisposable? _layoutChanges;
    private MachineryPanel? _panel;
    private LayoutElement? _holderLayout;
    private CustomUIElement? _status;

    internal TerminalModulesScreen(TerminalWindowHost window) : base(window) { }
    internal override string UnavailableKey => "terminal_modules_unavailable";
    internal override bool FitsContent => true;

    protected override bool BuildContent(CustomUIBuilder builder)
    {
        if (!ModuleBayHost.TryFindPanel(Window.Terminal, out var panel, out var row)) return false;
        _panel = panel;
        ResetNaturalSize(panel);
        var holder = new GameObject("module_bay", typeof(RectTransform)).GetComponent<RectTransform>();
        holder.SetParent(builder.CurrentContainer, false);
        _holderLayout = holder.gameObject.AddComponent<LayoutElement>();
        SetHolderSize();
        _bayHost = new ModuleBayHost(Window.WriteDiagnostic);
        if (!_bayHost.Show(panel, row, holder)) return false;
        builder.AddLabel(string.Empty);
        _status = builder.Last;
        RefreshStatus();
        _layoutChanges = ModuleBayExpansionHooks.Api?.SubscribeLayoutChanged((item, _) =>
        {
            if (!ReferenceEquals(item, Window.Terminal)) return;
            ResetNaturalSize(panel);
            SetHolderSize();
            RefreshStatus();
            Window.FitSubScreenContent(this);
        });
        return true;
    }

    private static void ResetNaturalSize(MachineryPanel panel)
    {
        MachineryContainerUtils.TryResize(panel.content, 0, 0);
        panel.ResizePixels(0, 0);
        panel.Validate();
    }

    private void SetHolderSize()
    {
        if (_panel == null || _holderLayout == null) return;
        var scale = 1f / TerminalWindowMetrics.NativeScale;
        _holderLayout.minWidth = _holderLayout.preferredWidth = _panel.widthPixels * scale;
        _holderLayout.minHeight = _holderLayout.preferredHeight = _panel.heightPixels * scale;
    }

    private void RefreshStatus()
    {
        if (_status == null) return;
        ModuleBayState? state = ModuleBayExpansionHooks.Api?.GetState(Window.Terminal);
        _status.SetText(state?.Available == true
            ? string.Format(Window.Text.Get("terminal_modules_stage"), state.Stage, state.MaximumStage,
                state.Definition!.Stage(state.Stage).Layout.AvailableCells)
            : Window.Text.Get(UnavailableKey));
    }

    internal override void Dispose()
    {
        _layoutChanges?.Dispose();
        _layoutChanges = null;
        _bayHost?.Dispose("modules_screen_closed");
        _bayHost = null;
        _holderLayout = null;
        _panel = null;
        _status = null;
        base.Dispose();
    }
}
