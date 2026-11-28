using System;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

internal static partial class CellPages
{
    internal static event Action<GameItem>? UsageChanged;

    internal static void NotifyLayoutWritten(GameItem cell) => UsageChanged?.Invoke(cell);

    internal static void ObserveRootChanges(GameGridInventory page)
    {
        // The grid's end callbacks run after it updates the root list and ownership. Defer reading until the move ends.
        void NotifyRootChange()
        {
            if (page.parent is MachineryPanelRow { parent: PixelWindow { parentItems.Count: 1 } window } row &&
                ReferenceEquals(window.childElement, row))
                UsageChanged?.Invoke(window.parentItems[0]);
        }

        page.onSlotAddItemEndFunc += (_, _, _) => NotifyRootChange();
        page.onSlotRemoveItemEndFunc += (item, _, _) =>
        {
            StoredRootKeys.Forget(item);
            NotifyRootChange();
        };
    }
}
