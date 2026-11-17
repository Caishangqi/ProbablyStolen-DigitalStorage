using System;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>A release-time inventory target, with an optional first-root cell on a visible grid.</summary>
internal sealed class InventoryDropTarget
{
    internal InventoryDropTarget(GameGridInventory inventory, GridDropAnchor? anchor = null)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Anchor = anchor;
    }

    internal GameGridInventory Inventory { get; }
    internal GridDropAnchor? Anchor { get; }
}
