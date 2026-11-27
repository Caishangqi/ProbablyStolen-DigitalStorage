using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>
/// A page on the terminal window's sub-screen stack: a back <see cref="TabButton"/> at the top left, then the page
/// content. A page pushed above it hides it without removing its controls, so it comes back as it was; the back
/// button and the window's back key return one page. Only the terminal window itself closes the window.
/// </summary>
internal abstract class SubScreen : ITerminalBackHandler, ITerminalScreen
{
    /// <summary>The message shown when a page cannot be opened and names no message of its own.</summary>
    internal const string DefaultUnavailableKey = "terminal_page_unavailable";

    private RectTransform? _container;

    protected SubScreen(TerminalWindowHost window)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <inheritdoc />
    public virtual string ScreenId => GetType().Name;

    /// <summary>The status message key shown on the view when this page cannot be opened.</summary>
    internal virtual string UnavailableKey => DefaultUnavailableKey;

    internal virtual bool FitsContent => false;

    /// <summary>The terminal window this page belongs to.</summary>
    protected TerminalWindowHost Window { get; }

    /// <summary>
    /// Builds the page into its own <paramref name="container"/>. Returns false when the page cannot be shown; the
    /// window then keeps the page or view below.
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

    /// <summary>Whether the page draws its own back button; none when the window's dock holds one for it.</summary>
    protected virtual bool AddBackButton => !Window.HasDockedBack(ScreenId);

    protected virtual void ReturnToParent() => Window.PopScreen();

    public virtual bool HandleBack() => false;

    /// <summary>
    /// Gets a fresh copy of this page to show again after the window rebuilds under it, or null when the page closes with
    /// the old window.
    /// </summary>
    internal virtual SubScreen? Reopen() => null;

    /// <summary>Runs when another page is pushed above this one; its controls stay and are only hidden.</summary>
    internal virtual void OnCovered()
    {
    }

    /// <summary>Runs when the page above returns; refresh anything that page may have changed.</summary>
    internal virtual void OnRevealed()
    {
    }

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
