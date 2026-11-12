using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Api.Networking;

/// <summary>Network lookups must run on the main thread.</summary>
public interface IGridHelper
{
    /// <summary>Finds the network of a terminal, drive or controller item.</summary>
    /// <returns>False when the item is not a network machine.</returns>
    bool TryGetNode(GameItem item, out IGridNode? node);

    /// <summary>
    /// Finds the network whose drive holds the cell that stores <paramref name="storedRoot"/> on one of its pages.
    /// </summary>
    /// <returns>False when the root is not on a cell page or the cell is not in a drive.</returns>
    bool TryGetNodeOfStored(GameItem storedRoot, out IGridNode? node);

    /// <summary>Lists the online networks that have a machine inside <paramref name="scope"/>; offline ones are left out.</summary>
    IReadOnlyList<IGrid> FindOnlineGrids(GraphNodeStorage scope);
}
