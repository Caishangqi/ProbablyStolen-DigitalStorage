using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Platform.Integration;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;

internal sealed class TerminalDecorationRefresh
{
    private readonly IntegrationDecorationCatalog _catalog;
    private readonly Func<Action, Action?> _nextFrame;
    private readonly Action<StorageScreenPage, bool> _draw;
    private readonly Action _clear;
    private bool _visible;
    private PendingRefresh? _pending;
    private StorageScreenPage? _displayedPage;
    private bool _drawn;
    private long _revision;
    private bool _storageAvailable;

    internal TerminalDecorationRefresh(IntegrationDecorationCatalog catalog, Func<Action, Action?> nextFrame,
        Action<StorageScreenPage, bool> draw, Action clear)
    {
        _catalog = catalog;
        _nextFrame = nextFrame;
        _draw = draw;
        _clear = clear;
    }

    internal void Show()
    {
        if (!_visible)
        {
            _visible = true;
            _catalog.Changed += Enqueue;
        }
        Redraw();
    }

    internal void Hide()
    {
        if (!_visible) return;
        _visible = false;
        _catalog.Changed -= Enqueue;
        _drawn = false;
        var pending = _pending;
        _pending = null;
        try { pending?.Cancel?.Invoke(); }
        finally { _clear(); }
    }

    internal void SetDisplayedPage(StorageScreenPage page, bool storageAvailable)
    {
        if (!ReferenceEquals(_displayedPage, page) || _storageAvailable != storageAvailable) _drawn = false;
        _displayedPage = page;
        _storageAvailable = storageAvailable;
        Redraw();
    }

    internal void SamplesChanged()
    {
        if (!_storageAvailable || !_catalog.HasActive) return;
        _drawn = false;
        Enqueue();
    }

    private void Redraw()
    {
        if (!_visible || _displayedPage is not { } page) return;
        var revision = _catalog.Revision;
        if (_drawn && (_revision == revision || _pending != null)) return;
        _revision = revision;
        _drawn = true;
        try { _draw(page, _storageAvailable); }
        catch
        {
            _drawn = false;
            throw;
        }
    }

    private void Enqueue()
    {
        if (!_visible || _pending != null) return;
        var pending = new PendingRefresh();
        _pending = pending;
        try
        {
            pending.Cancel = _nextFrame(() =>
            {
                if (!ReferenceEquals(_pending, pending)) return;
                _pending = null;
                Redraw();
            });
        }
        finally
        {
            if (pending.Cancel == null && ReferenceEquals(_pending, pending)) _pending = null;
        }
    }

    private sealed class PendingRefresh
    {
        internal Action? Cancel { get; set; }
    }
}
