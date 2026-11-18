using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Displays targets in the inventory's native highlight nodes.</summary>
internal sealed class TerminalTargetHoverView : IDisposable
{
    private const float RetryInterval = 0.25f;
    private readonly List<HighlightLease> _highlights = new();
    private SlotMarker? _marker;
    private int _expectedNodes;
    private float _nextRetry;
    private GameInventory? _inventory;
    private GameItem? _item;
    private GridShape? _shape;
    private GridDropAnchor? _anchor;
    private RenderHandler.BackgroundColor? _color;

    internal void Show(GameItemElement item, RenderHandler.BackgroundColor color) =>
        Show(item.parentInventory, item, null, color);

    internal void Show(InventoryDropTarget target, bool useAnchor, RenderHandler.BackgroundColor color) =>
        Show(target.HoveredItem?.parentInventory ?? (target.HoveredItem == null ? target.Inventory : null),
            target.HoveredItem, useAnchor ? target.Anchor : null, color);

    private void Show(GameInventory? inventory, GameItemElement? item, GridDropAnchor? anchor,
        RenderHandler.BackgroundColor color)
    {
        if (inventory is not (GameGridInventory or GameSlotInventory) || RenderHandler.current == null ||
            (item != null && item.IsDestroyed()))
        {
            Hide();
            return;
        }

        var shape = item?.modifiedShape ?? (inventory as GameGridInventory)?.inventoryShape;
        if (ReferenceEquals(_inventory, inventory) && ReferenceEquals(_item, item) &&
            ReferenceEquals(_shape, shape) && Nullable.Equals(_anchor, anchor) && _color == color) return;

        Hide();
        SlotMarker? marker;
        if (item != null)
        {
            marker = item.GetIncompleteSlotInInventory();
        }
        else if (inventory is GameGridInventory grid)
        {
            // Use the same footprint as HighlightWholeInventory, with doReplace disabled to preserve native highlights.
            var footprint = anchor is { } cell
                ? new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(cell.X, cell.Y).Build()
                : new GridShapeBuilder(grid.inventoryShape.width, grid.inventoryShape.height).SetDataFill(1).Build();
            marker = new SlotMarker { inventory = grid, itemGridShape = footprint };
        }
        else if (inventory is GameSlotInventory)
        {
            marker = new SlotMarker { inventory = inventory };
        }
        else return;

        _marker = marker;
        _inventory = inventory;
        _item = item;
        _shape = shape;
        _anchor = anchor;
        _color = color;
        if (marker == null || (inventory is GameGridInventory && marker.itemGridShape == null)) return;
        _expectedNodes = inventory is GameGridInventory targetGrid
            ? CountVisibleCells(targetGrid.inventoryShape, marker.itemGridShape)
            : 1;
        if (_expectedNodes > 0) AcquireHighlights();
    }

    internal void RetryIncompleteHighlight()
    {
        if (_expectedNodes == 0 || Time.unscaledTime < _nextRetry || RenderHandler.current == null) return;
        _nextRetry = Time.unscaledTime + RetryInterval;
        for (var index = _highlights.Count - 1; index >= 0; index--)
        {
            var lease = _highlights[index];
            if (lease.IsVisible) continue;
            lease.Dispose();
            _highlights.RemoveAt(index);
        }
        if (_highlights.Count < _expectedNodes) AcquireHighlights();
    }

    private void AcquireHighlights()
    {
        foreach (var node in _inventory!.HighlightSlot(_marker!, _color!.Value, doReplace: false))
            if (node != null && node.image != null && node.image.sprite != null)
                _highlights.Add(new HighlightLease(node.image));
    }

    private static int CountVisibleCells(GridShape inventoryShape, GridShape footprint)
    {
        var count = 0;
        for (var y = 0; y < footprint.height; y++)
        for (var x = 0; x < footprint.width; x++)
        {
            if (footprint.GetLocal(x, y) == 0) continue;
            var (column, row) = footprint.ForwardTransform(x, y);
            if (!inventoryShape.Intersects(column, row)) count++;
        }
        return count;
    }

    internal void Hide()
    {
        foreach (var highlight in _highlights) highlight.Dispose();
        _highlights.Clear();
        _marker = null;
        _expectedNodes = 0;
        _nextRetry = 0;
        _inventory = null;
        _item = null;
        _shape = null;
        _anchor = null;
        _color = null;
    }

    public void Dispose() => Hide();

    private sealed class HighlightLease : IDisposable
    {
        private readonly Image _image;
        private readonly Sprite _sprite;
        private bool _released;

        internal HighlightLease(Image image)
        {
            _image = image;
            _sprite = image.sprite;
            // Render the native sprite through the override while leaving the base slot empty. A native color
            // write always fills the base slot, even when it writes the same color, and takes the node back.
            image.overrideSprite = _sprite;
            image.sprite = null;
            image.RegisterDirtyVerticesCallback(OnImageChanged);
        }

        internal bool IsVisible => !_released && _image != null && _image.enabled && _image.sprite == null;

        private void OnImageChanged()
        {
            if (_image.sprite != null) Dispose();
        }

        public void Dispose()
        {
            if (_released || _image == null) return;
            _released = true;
            _image.UnregisterDirtyVerticesCallback(OnImageChanged);
            if (_image.sprite == null)
            {
                _image.enabled = false;
                _image.sprite = _sprite;
            }
            _image.overrideSprite = null;
        }
    }
}
