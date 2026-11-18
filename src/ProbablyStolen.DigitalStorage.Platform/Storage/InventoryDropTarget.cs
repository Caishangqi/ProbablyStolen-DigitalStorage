using System;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>A release-time inventory target with an optional grid anchor and hovered container item.</summary>
internal sealed class InventoryDropTarget
{
    private readonly TerminalDiagnostics? _diagnostics;

    internal InventoryDropTarget(GameInventory inventory, GridDropAnchor? anchor = null, Vector2? worldPointer = null,
        TerminalDiagnostics? diagnostics = null, GameItemElement? hoveredItem = null, bool isAnchorValid = true)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Anchor = anchor;
        WorldPointer = worldPointer;
        _diagnostics = diagnostics;
        HoveredItem = hoveredItem;
        IsAnchorValid = isAnchorValid;
    }

    internal GameInventory Inventory { get; }
    internal GridDropAnchor? Anchor { get; }
    internal Vector2? WorldPointer { get; }
    internal GameItemElement? HoveredItem { get; }
    internal bool IsAnchorValid { get; }
    internal InventoryDropTarget WithoutAnchor() => Anchor.HasValue || !IsAnchorValid
        ? new InventoryDropTarget(Inventory, diagnostics: _diagnostics, hoveredItem: HoveredItem)
        : this;

    internal bool MayAccept(GameItem item) => item != null && IsUsable(Inventory, HoveredItem) &&
        Inventory.MayHaveValidInventorySlot(item);

    internal static bool IsUsable(GameInventory inventory, GameItemElement? container)
    {
        if (inventory.IsInsertLocked() || !GeneralHelper.MayPlayerInsertInto(inventory)) return false;
        if (container != null)
            return !container.IsDestroyed() && GeneralHelper.MayPlayerInsertInto(container) &&
                ReferenceEquals(GraphUtils.FindParentType<GameItem>(inventory), container);

        return inventory switch
        {
            GameGridInventory grid => grid.handler != null && grid.handler.gameObject.activeInHierarchy,
            GameSlotInventory slot => slot.handler != null && slot.handler.gameObject.activeInHierarchy,
            _ => false
        };
    }

    internal bool? PreviewWholeAcceptance(GameItem item, bool useAnchor)
    {
        if (item == null || item.unitCount <= 0 || !MayAccept(item)) return false;

        if (Inventory is GameSlotInventory slot)
        {
            var sourceShape = item.modifiedShape;
            var placedShape = new GridShapeBuilder(sourceShape)
                .SetTransform(0, 0, flipped: false, slot.defaultOrientation).Build();
            var marker = new SlotMarker { item = item, inventory = slot, itemGridShape = placedShape };
            var children = slot.childItems;
            return children.Count <= 1 &&
                (children.Count == 0 || ReferenceEquals(children[0], item)) &&
                item.CheckIsStateValid(slot, marker, sourceShape, item.modifiedState) &&
                slot.CheckAcceptParentShapeStateModifier(item, marker, sourceShape, item.modifiedState) &&
                item.MaxNumAdd(marker) >= item.unitCount;
        }

        if (Inventory is not GameGridInventory grid) return false;
        // Native auto-placement prioritizes passthrough inventories and stacking before empty footprints.
        if (!useAnchor || Anchor is not { } anchor) return null;
        var shape = new GridShapeBuilder(item.modifiedShape).SetPosition(anchor.X, anchor.Y).Build();
        if (!shape.FullyIntersects(grid.inventoryShape, (byte value, byte occupied) => value != 1 || occupied == 0))
            return false;
        foreach (var other in grid.childItems)
            if (!ReferenceEquals(other, item) && shape.Intersects(other.modifiedShape)) return false;

        var gridMarker = new SlotMarker { item = item, inventory = grid, itemGridShape = shape };
        return item.CheckIsStateValid(grid, gridMarker, shape, item.modifiedState) &&
            grid.CheckAcceptParentShapeStateModifier(item, gridMarker, shape, item.modifiedState) &&
            item.MaxNumAdd(gridMarker) >= item.unitCount;
    }

    internal InventoryDropTarget? ForSinglePlacement(long requestedPieces) =>
        requestedPieces == 1 && Anchor.HasValue ? this : null;

    internal Vector2 CenterFor(GridShape shape)
    {
        var anchor = Anchor ?? throw new InvalidOperationException("Point placement requires a visible grid.");
        // Native slot placement takes a center, but the highlighted cell anchors the top-left of the shape.
        var center = anchor.FootprintCenter(UISettings.current.gridSize,
            shape.maxX - shape.minX + 1, shape.maxY - shape.minY + 1);
        var grid = Inventory as GameGridInventory ??
            throw new InvalidOperationException("Point placement requires a grid inventory.");
        return grid.rectTransform.TransformPoint(new Vector3(center.X, -center.Y, 0f));
    }

    internal void RecordPlacement(string stage, GridShape? shape = null, Vector2? center = null, SlotMarker? slot = null,
        int? requestedUnits = null, int acceptedUnits = 0)
    {
        if (_diagnostics == null || WorldPointer is not { } pointer) return;
        var details = StorageDiagnostics.Context(() =>
        {
            var gridInventory = Inventory as GameGridInventory;
            var rect = gridInventory?.rectTransform;
            if (rect == null) return $"inventory={Inventory.GetType().Name}";
            var size = (float)UISettings.current.gridSize;
            var localPointer = rect.InverseTransformPoint(pointer);
            var localCenter = center.HasValue ? rect.InverseTransformPoint(center.Value) : (Vector3?)null;
            var centerText = localCenter is { } value
                ? FormattableString.Invariant($"{value.x / size:F2},{-value.y / size:F2}") : "none";
            var bounds = slot?.itemGridShape;
            var boundsText = bounds == null ? "none" : ReferenceEquals(bounds, shape) ? "unpositioned"
                : $"{bounds.minX},{bounds.minY}..{bounds.maxX},{bounds.maxY}";
            var shapeText = shape == null ? "footprint=unknown" :
                $"footprint={shape.maxX - shape.minX + 1}x{shape.maxY - shape.minY + 1} orientation={shape.orientation} flipped={shape.flipped}";
            var grid = gridInventory != null
                ? $"grid={gridInventory.inventoryShape.width}x{gridInventory.inventoryShape.height}"
                : $"inventory={Inventory.GetType().Name}";
            return FormattableString.Invariant(
                $"{grid} pointer={localPointer.x / size:F2},{-localPointer.y / size:F2} anchor={Anchor?.X},{Anchor?.Y} center={centerText} {shapeText} slot_bounds={boundsText}");
        });
        _diagnostics.Record("extract_grid_placement", FormattableString.Invariant(
            $"stage={stage} {details} requested_units={requestedUnits?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} offered_units={slot?.numTransfer ?? 0} accepted_units={acceptedUnits}"));
    }
}
