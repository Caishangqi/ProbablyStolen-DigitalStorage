using System;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Uses the native highlight pool to preview a single landing cell or an auto-placement grid.</summary>
internal sealed class TerminalDropAnchorView : IDisposable
{
    private readonly HighlightManager _highlights = new();
    private GameGridInventory? _inventory;
    private GridShape? _inventoryShape;
    private GridDropAnchor? _anchor;

    internal void Show(InventoryDropTarget? target, bool useAnchor)
    {
        if (target?.Anchor is not { } anchor || RenderHandler.current == null || target.Inventory.handler == null)
        {
            Hide();
            return;
        }
        var inventoryShape = target.Inventory.inventoryShape;
        GridDropAnchor? previewCell = useAnchor ? anchor : null;
        if (ReferenceEquals(_inventory, target.Inventory) && ReferenceEquals(_inventoryShape, inventoryShape) &&
            Nullable.Equals(_anchor, previewCell)) return;

        // Unlike HighlightWholeInventory, HighlightManager doesn't replace another feature's active nodes.
        var shape = useAnchor
            ? new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(anchor.X, anchor.Y).Build()
            : new GridShapeBuilder(inventoryShape.width, inventoryShape.height).SetDataFill(1).Build();
        _highlights.Try(new SlotMarker
        {
            inventory = target.Inventory,
            itemGridShape = shape
        }, RenderHandler.BackgroundColor.Green);
        _inventory = target.Inventory;
        _inventoryShape = inventoryShape;
        _anchor = previewCell;
    }

    internal void Hide()
    {
        _highlights.Clear();
        _inventory = null;
        _inventoryShape = null;
        _anchor = null;
    }

    public void Dispose() => Hide();
}
