using System;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>A release-time inventory target with an optional anchor on a visible grid.</summary>
internal sealed class InventoryDropTarget
{
    private readonly TerminalDiagnostics? _diagnostics;

    internal InventoryDropTarget(GameGridInventory inventory, GridDropAnchor? anchor = null, Vector2? worldPointer = null,
        TerminalDiagnostics? diagnostics = null)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Anchor = anchor;
        WorldPointer = worldPointer;
        _diagnostics = diagnostics;
    }

    internal GameGridInventory Inventory { get; }
    internal GridDropAnchor? Anchor { get; }
    internal Vector2? WorldPointer { get; }

    internal InventoryDropTarget? ForSinglePlacement(long requestedPieces) =>
        requestedPieces == 1 && Anchor.HasValue ? this : null;

    internal Vector2 CenterFor(GridShape shape)
    {
        var anchor = Anchor ?? throw new InvalidOperationException("Point placement requires a visible grid.");
        // Native slot placement takes a center, but the highlighted cell anchors the top-left of the shape.
        var center = anchor.FootprintCenter(UISettings.current.gridSize,
            shape.maxX - shape.minX + 1, shape.maxY - shape.minY + 1);
        return Inventory.rectTransform.TransformPoint(new Vector3(center.X, -center.Y, 0f));
    }

    internal void RecordPlacement(string stage, GridShape? shape = null, Vector2? center = null, SlotMarker? slot = null,
        int? requestedUnits = null, int acceptedUnits = 0)
    {
        if (_diagnostics == null || WorldPointer is not { } pointer) return;
        var details = StorageDiagnostics.Context(() =>
        {
            var rect = Inventory.rectTransform;
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
            return FormattableString.Invariant(
                $"grid={Inventory.inventoryShape.width}x{Inventory.inventoryShape.height} pointer={localPointer.x / size:F2},{-localPointer.y / size:F2} anchor={Anchor?.X},{Anchor?.Y} center={centerText} {shapeText} slot_bounds={boundsText}");
        });
        _diagnostics.Record("extract_grid_placement", FormattableString.Invariant(
            $"stage={stage} {details} requested_units={requestedUnits?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} offered_units={slot?.numTransfer ?? 0} accepted_units={acceptedUnits}"));
    }
}
