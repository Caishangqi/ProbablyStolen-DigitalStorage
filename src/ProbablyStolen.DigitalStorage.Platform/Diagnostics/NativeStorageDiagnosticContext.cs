using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal static class NativeStorageDiagnosticContext
{
    internal static string Item(GameItem? item)
    {
        GameItem? host = null;
        try { host = item?.parentInventory?.GetParentItem(); }
        catch { }
        return string.Join(" ",
            StorageDiagnostics.Field("item", () => item?.identifier),
            StorageDiagnostics.Field("uid", () => item?.uniqueId),
            StorageDiagnostics.Field("parent", () => item?.parentInventory?.identifier),
            StorageDiagnostics.Field("host", () => host?.identifier),
            StorageDiagnostics.Field("host_uid", () => host?.uniqueId));
    }

    internal static string Cell(GameItem cell) => Item(cell) + " " + string.Join(" ",
        StorageDiagnostics.Field("schema", () => CellTags.ReadSchema(cell)),
        StorageDiagnostics.Field("health", () => CellHealth.Get(cell).Status),
        StorageDiagnostics.Field("reason", () => CellHealth.Get(cell).Reason),
        StorageDiagnostics.Field("pages", () => CellPages.Tabs(cell)?.count),
        StorageDiagnostics.Field("recorded_pages", () => CellTags.TryReadLayout(cell, out var layout) ? layout!.Pages.Count : -1),
        StorageDiagnostics.Field("stage", () => CellUpgradeInventory.Stage(cell)),
        StorageDiagnostics.Field("modules", () => CellUpgradeInventory.Bay(cell)?.childItems.Count));
}
