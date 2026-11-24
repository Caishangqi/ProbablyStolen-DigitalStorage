using System;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed partial class IoPortItem
{
    internal const string ItemId = "digital_storage.io_port";
    private const string BayPrefix = "digital_storage.io_port_bay.";

    internal static bool IsCellInBay(GameItem cell) =>
        cell.parentInventory is GameSlotInventory slot && ReferenceEquals(slot.childItem, cell) &&
        slot.identifier?.StartsWith(BayPrefix, StringComparison.Ordinal) == true &&
        slot.GetParentItem()?.identifier == ItemId;
}
