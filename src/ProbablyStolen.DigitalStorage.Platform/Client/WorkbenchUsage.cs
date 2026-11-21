using System;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Owns the usage previews of this composition's workbench slots.</summary>
internal sealed class WorkbenchUsage
{
    private readonly Action<Exception> _reportError;
    private readonly ConditionalWeakTable<GameSlotInventory, Slot> _slots = new();

    internal WorkbenchUsage(Action<Exception> reportError) => _reportError = reportError;

    internal Slot Register(GameSlotInventory inventory, GameItem workbench, PixelWindow window,
        Func<bool> isVisible, Action refreshSummary)
    {
        var slot = new Slot(inventory, workbench, window, isVisible, refreshSummary, _reportError);
        _slots.Add(inventory, slot);
        return slot;
    }

    internal void Detach()
    {
        foreach (var entry in _slots) entry.Value.Detach(redrawVisible: true);
    }

    internal CellModuleUsage? Find(GameItem cell) => FindSlot(cell)?.Get(cell);

    internal void RefreshSummary(GameItem cell) => FindSlot(cell)?.RefreshSummary();

    internal void InvalidateContents(GameItem cell)
    {
        if (FindSlot(cell) is not { } slot) return;
        CellInventoryCache.Forget(cell);
        slot.Invalidate();
    }

    private Slot? FindSlot(GameItem cell) =>
        cell.parentInventory is GameSlotInventory inventory && ReferenceEquals(inventory.childItem, cell) &&
        _slots.TryGetValue(inventory, out var slot) && !slot.IsDetached ? slot : null;

    internal sealed class Slot
    {
        private readonly GameSlotInventory _inventory;
        private readonly GameItem _workbench;
        private readonly PixelWindow _window;
        private readonly Func<bool> _isVisible;
        private readonly Action _refreshSummary;
        private readonly Action<Exception> _reportError;
        private GameItem? _cell;
        private CellModuleUsage _usage;
        private int _revision;

        internal Slot(GameSlotInventory inventory, GameItem workbench, PixelWindow window, Func<bool> isVisible,
            Action refreshSummary, Action<Exception> reportError)
        {
            _inventory = inventory;
            _workbench = workbench;
            _window = window;
            _isVisible = isVisible;
            _refreshSummary = refreshSummary;
            _reportError = reportError;
        }

        internal bool IsDetached { get; private set; }

        internal void RefreshSummary() => _refreshSummary();

        internal void Invalidate()
        {
            _cell = null;
            _usage = default;
            _revision++;
        }

        internal void Detach(bool redrawVisible = false)
        {
            if (IsDetached) return;
            var redraw = redrawVisible && VisibleCell != null;
            IsDetached = true;
            Invalidate();
            // A live stop can leave the native window open after the preview is detached.
            if (redraw) _refreshSummary();
        }

        private GameItem? VisibleCell
        {
            get
            {
                if (IsDetached || !_isVisible() || !ReferenceEquals(_workbench.contentWindow, _window)) return null;
                // Machinery inventories are linked to the panel row, which is attached to the window.
                if (_inventory.parent is not MachineryPanelRow row || !ReferenceEquals(_window.childElement, row) ||
                    !ReferenceEquals(row.parent, _window)) return null;
                return _inventory.childItem is { } cell && ReferenceEquals(cell.parentInventory, _inventory) ? cell : null;
            }
        }

        internal CellModuleUsage? Get(GameItem cell)
        {
            if (SaveManager.IsDecoding || !ReferenceEquals(VisibleCell, cell)) return null;
            if (CellHealth.IsUnavailable(cell)) return CellModuleUsage.Unavailable(layoutUnavailable: true);
            return ReferenceEquals(_cell, cell) ? _usage : default(CellModuleUsage);
        }

        internal bool Refresh()
        {
            var cell = VisibleCell;
            if (cell == null || SaveManager.IsDecoding)
            {
                if (_cell == null) return false;
                Invalidate();
                return true;
            }
            if (ReferenceEquals(_cell, cell)) return false;

            var revision = _revision;
            var usage = CellUpgradeInventory.ReadUsage(cell, ReadCached, _reportError);
            if (_revision != revision || SaveManager.IsDecoding || !ReferenceEquals(VisibleCell, cell)) return false;
            _cell = cell;
            _usage = usage;
            return true;
        }

        private static CellView ReadCached(GameItem cell) => CellPages.ReadCached(cell, CellWriteState.ExtractOnly);
    }
}
