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
    internal static IReadOnlyList<GameItem> GetCellItems(GameItem drive) =>
        GetBays(drive).Select(bay => bay.childItem).Where(IsMountedCell).Select(cell => cell!).ToArray();

    /// <summary>Gets every cell bay slot of a drive, empty or not, in bay order.</summary>
    internal static IReadOnlyList<GameSlotInventory> GetBays(GameItem drive)
    {
        if (drive == null)
        {
            throw new ArgumentNullException(nameof(drive));
        }

        return GraphUtils.FindAllChildrenType<GameSlotInventory>(drive)
            .Where(slot => slot.identifier != null &&
                slot.identifier.StartsWith(CellContentWindow.BayPrefix, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>Gets whether a bay holds an assembled storage cell with its storage component.</summary>
    internal static bool IsMountedCell(GameItem? cell)
    {
        if (cell == null || cell.identifier != StorageCellDisassemblyRecipe.CellId)
        {
            return false;
        }

        var component = CellContentWindow.Component(cell);
        return component != null && component.identifier == StorageComponent.ItemId;
    }

    private sealed class MountedCell : IStorageCell
    {
        internal static readonly MountedCell Instance = new();

        public CellState GetStatus() => CellState.Empty;
    }
}
