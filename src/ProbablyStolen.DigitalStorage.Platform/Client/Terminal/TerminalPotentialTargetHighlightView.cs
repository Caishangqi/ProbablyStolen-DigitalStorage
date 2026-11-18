using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Shows blue candidate slots behind single-item drag feedback using native inventory highlights.</summary>
internal sealed class TerminalPotentialTargetHighlightView : IDisposable
{
    private readonly Dictionary<GameItemElement, TerminalTargetHoverView> _active = new();
    private readonly Dictionary<GameInventory, List<GameItemElement>> _byInventory = new();
    private readonly Stack<TerminalTargetHoverView> _pool = new();
    private readonly Action<string> _diagnostic;
    private GameItemElement? _coveredItem;
    private GameInventory? _coveredInventory;

    internal TerminalPotentialTargetHighlightView(Action<string> diagnostic) =>
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));

    internal void Show(GameItem source)
    {
        Hide();
        var windows = WindowsHandler.current?.visibleWindows;
        if (windows == null) return;

        var seen = new HashSet<GameItem>();
        foreach (var window in windows)
        foreach (var candidate in GraphUtils.FindChildrenType(window, (GameItem item) => item is GameItemElement))
        {
            if (candidate is not GameItemElement item || ReferenceEquals(item, source) || !seen.Add(item) ||
                item.IsDestroyed() || item.parentInventory == null) continue;

            var parent = item.parentInventory;
            try
            {
                var destination = TerminalDragTargetResolver.ResolveContainerInventory(item.child);
                if (destination == null || !new InventoryDropTarget(destination, hoveredItem: item).MayAccept(source))
                    continue;
                var highlight = _pool.Count == 0 ? new TerminalTargetHoverView() : _pool.Pop();
                _active.Add(item, highlight);
                if (!_byInventory.TryGetValue(parent, out var items))
                    _byInventory.Add(parent, items = new List<GameItemElement>());
                items.Add(item);
                ShowBlue(item, highlight);
            }
            catch (Exception error) when (error is not OutOfMemoryException && error is not StackOverflowException &&
                                          error is not AccessViolationException)
            {
                _diagnostic($"terminal_drag_target_probe_failed error={error.GetType().Name}");
            }
        }
    }

    internal void SetCovered(GameItemElement? item, GameInventory? inventory, TerminalTargetHoverView hover)
    {
        if (ReferenceEquals(_coveredItem, item) && ReferenceEquals(_coveredInventory, inventory)) return;
        hover.Hide();
        if (_coveredItem != null)
            UpdateCoverage(_coveredItem, item, inventory, hide: false);
        else if (_coveredInventory != null && _byInventory.TryGetValue(_coveredInventory, out var previous))
            foreach (var candidate in previous) UpdateCoverage(candidate, item, inventory, hide: false);

        if (item != null)
            UpdateCoverage(item, _coveredItem, _coveredInventory, hide: true);
        else if (inventory != null && _byInventory.TryGetValue(inventory, out var next))
            foreach (var candidate in next) UpdateCoverage(candidate, _coveredItem, _coveredInventory, hide: true);
        _coveredItem = item;
        _coveredInventory = inventory;
    }

    private void UpdateCoverage(GameItemElement candidate, GameItemElement? otherItem,
        GameInventory? otherInventory, bool hide)
    {
        if (!_active.TryGetValue(candidate, out var highlight) || Covers(candidate, otherItem, otherInventory))
            return;
        if (hide) highlight.Hide();
        else if (!candidate.IsDestroyed()) ShowBlue(candidate, highlight);
    }

    internal void Hide()
    {
        foreach (var highlight in _active.Values)
        {
            highlight.Hide();
            _pool.Push(highlight);
        }
        _active.Clear();
        _byInventory.Clear();
        _coveredItem = null;
        _coveredInventory = null;
    }

    public void Dispose()
    {
        Hide();
        _pool.Clear();
    }

    private static bool Covers(GameItemElement candidate, GameItemElement? item, GameInventory? inventory) =>
        ReferenceEquals(candidate, item) || (inventory != null && ReferenceEquals(candidate.parentInventory, inventory));

    private static void ShowBlue(GameItemElement item, TerminalTargetHoverView highlight) =>
        highlight.Show(item, RenderHandler.BackgroundColor.Blue);
}
