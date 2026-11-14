using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using TMPro;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Diagnostics;

/// <summary>
/// The diagnostic test view: placeholder text only, so view switching and view-owned rail and picker buttons can be
/// checked in game before any module exists. It reads and changes no items.
/// </summary>
internal sealed class DiagnosticTerminalView : ITerminalView
{
    private readonly TerminalWindow _window;
    private RectTransform? _container;

    internal DiagnosticTerminalView(TerminalWindow window) =>
        _window = window ?? throw new ArgumentNullException(nameof(window));

    /// <inheritdoc />
    public void Build(RectTransform container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window.CustomWindow, container);
        DiagnosticText.Add(builder, _window.Text.Get("terminal_diagnostics_view_text"));
    }

    /// <inheritdoc />
    public void Resume()
    {
    }

    /// <inheritdoc />
    public void Suspend()
    {
    }

    /// <inheritdoc />
    public void OnReturnFromSubScreen()
    {
    }

    /// <inheritdoc />
    public object? StoreState() => null;

    /// <inheritdoc />
    public void RestoreState(object? state)
    {
    }

    /// <inheritdoc />
    public void SettingChanged(string settingName)
    {
    }

    /// <inheritdoc />
    public void ShowMessage(string textKey)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_container != null) CustomUIBuilder.ClearContainer(_container);
        _container = null;
    }
}

/// <summary>The placeholder page opened by the diagnostic picker button.</summary>
internal sealed class DiagnosticScreen : SubScreen
{
    internal DiagnosticScreen(TerminalWindow window) : base(window)
    {
    }

    /// <inheritdoc />
    protected override bool BuildContent(CustomUIBuilder builder)
    {
        DiagnosticText.Add(builder, Window.Text.Get("terminal_diagnostics_screen_text"));
        return true;
    }
}

/// <summary>A wrapped placeholder line for the diagnostic view and page.</summary>
internal static class DiagnosticText
{
    internal static void Add(CustomUIBuilder builder, string text)
    {
        builder.AddLabel(text);
        TerminalControls.SetSize(builder.Last, TerminalWindowMetrics.GridWidth, 40f);
        if (builder.Last.text == null) return;
        builder.Last.text.fontSize = 14f;
        builder.Last.text.enableWordWrapping = true;
        builder.Last.text.alignment = TextAlignmentOptions.TopLeft;
    }
}
