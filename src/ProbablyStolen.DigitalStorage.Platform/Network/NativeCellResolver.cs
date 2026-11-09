using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Platform.Network;

/// <summary>Finds the assembled storage cells mounted in a drive's bays, in bay order.</summary>
internal static class NativeCellResolver
{
    /// <summary>Gets grid-status cells for a drive; per-cell availability is checked by the storage operations.</summary>
    internal static IReadOnlyList<IStorageCell> Get(GameItem drive) =>
        GetCellItems(drive).Select(_ => (IStorageCell)MountedCell.Instance).ToArray();

    /// <summary>Gets the native cell items mounted in a drive, in bay order.</summary>
    internal static IReadOnlyList<GameItem> GetCellItems(GameItem drive)
    {
        if (drive == null)
        {
            throw new ArgumentNullException(nameof(drive));
        }

        var cells = new List<GameItem>();
        foreach (var slot in GraphUtils.FindAllChildrenType<GameSlotInventory>(drive))
        {
            var cell = slot.childItem;
            if (slot.identifier == null || !slot.identifier.StartsWith(CellContentWindow.BayPrefix,
                StringComparison.Ordinal) || cell == null || cell.identifier != StorageCellDisassemblyRecipe.CellId)
            {
                continue;
            }

            var component = CellContentWindow.Component(cell);
            if (component == null || component.identifier != StorageComponent.ItemId)
            {
                continue;
            }

            cells.Add(cell);
        }

        return cells;
    }

    private sealed class MountedCell : IStorageCell
    {
        internal static readonly MountedCell Instance = new();

        public CellState GetStatus() => CellState.Empty;
    }
}
