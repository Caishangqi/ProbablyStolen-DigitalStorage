using System;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Uses PS's native highlight pool to show only the intended first-root cell.</summary>
internal sealed class TerminalDropAnchorView : IDisposable
{
    private readonly HighlightManager _highlights = new();
    private GameGridInventory? _inventory;
    private GridDropAnchor? _anchor;

    internal void Show(InventoryDropTarget? target)
    {
        if (target?.Anchor is not { } anchor || RenderHandler.current == null || target.Inventory.handler == null)
        {
            Hide();
            return;
        }
        if (ReferenceEquals(_inventory, target.Inventory) && _anchor.HasValue && _anchor.Value.Equals(anchor)) return;

        // HighlightManager only records nodes acquired with doReplace:false; Clear leaves other highlights alone.
        _highlights.Try(new SlotMarker
        {
            inventory = target.Inventory,
            itemGridShape = new GridShapeBuilder(1, 1).SetDataFill(1).SetPosition(anchor.X, anchor.Y).Build()
        }, RenderHandler.BackgroundColor.Green);
        _inventory = target.Inventory;
        _anchor = anchor;
    }

    internal void Hide()
    {
        _highlights.Clear();
        _inventory = null;
        _anchor = null;
    }

    public void Dispose() => Hide();
}
