using System;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>
/// A page that temporarily replaces the terminal body and returns to the view it came from: a back
/// <see cref="TabButton"/> at the top left, then the page content. The window's back key also returns here; only the
/// terminal window itself closes the window.
/// </summary>
internal abstract class SubScreen : ITerminalBackHandler
{
    /// <summary>The message shown when a page cannot be opened and names no message of its own.</summary>
    internal const string DefaultUnavailableKey = "terminal_page_unavailable";

    private RectTransform? _container;

    protected SubScreen(TerminalWindowHost window)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>The status message key shown on the view when this page cannot be opened.</summary>
    internal virtual string UnavailableKey => DefaultUnavailableKey;

    internal virtual bool FitsContent => false;

    /// <summary>The terminal window this page belongs to.</summary>
    protected TerminalWindowHost Window { get; }

    /// <summary>
    /// Builds the page into <paramref name="container"/>. Returns false when the page cannot be shown; the window then
    /// returns to the view at once.
    /// </summary>
    internal bool Show(RectTransform container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        var builder = new CustomUIBuilder(CustomUIManager.Instance, Window.CustomWindow, container);
        builder.BeginColumn(spacing: 6f);
        if (AddBackButton) TabButton.Add(builder, Window.Text.Get("terminal_back"), ReturnToParent);
        var shown = BuildContent(builder);
        builder.End();
        return shown;
    }

    /// <summary>Builds the content below the back button; false when the page cannot be shown.</summary>
    protected abstract bool BuildContent(CustomUIBuilder builder);

    protected virtual bool AddBackButton => true;

    protected virtual void ReturnToParent() => Window.ReturnToParent();

    public virtual bool HandleBack() => false;

    internal void RefreshContent()
    {
        if (_container == null) return;
        CustomUIBuilder.ClearContainer(_container);
        Show(_container);
    }

    /// <summary>Releases anything the page borrowed, then removes its controls. Safe to call more than once.</summary>
    internal virtual void Dispose()
    {
        if (_container != null) CustomUIBuilder.ClearContainer(_container);
        _container = null;
    }
}
