using System.Runtime.CompilerServices;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Makes items dropped onto a machine icon fill its bays row by row, left to right. The native grid picks the bay with
/// the highest slot priority and, among equal ones, the last; every bay has the same priority, so cells went to the end
/// of the second row first. For the registered bay grids the highest priority still wins, and among equal ones the
/// first bay does.
/// </summary>
public static class ForwardFillGrids
{
    private static readonly ConditionalWeakTable<GridPixelElement, object> Grids = new();

    /// <summary>Fills <paramref name="grid"/> from its first bay.</summary>
    internal static void Register(GridPixelElement grid)
    {
        if (grid != null) Grids.AddOrUpdate(grid, grid);
    }

    /// <summary>
    /// Called before <c>GridPixelElement.TryFindOneValidInventorySlot</c>; returns true with the bay to use when the grid
    /// is registered, so the native search is skipped.
    /// </summary>
    public static bool TryFind(GridPixelElement grid, GameItem item, out SlotMarker? result)
    {
        result = null;
        if (grid == null || !Grids.TryGetValue(grid, out _)) return false;
        foreach (var child in grid.children)
        {
            var marker = child?.TryFindOneValidInventorySlot(item);
            if (marker != null && marker.numTransfer > 0 && (result == null || marker.priority > result.priority))
                result = marker;
        }

        return true;
    }
}
