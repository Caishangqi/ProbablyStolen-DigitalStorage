using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Platform.NativeContent;

internal static class ContentSnapshots
{
    internal static PhysicalItemSnapshot Item(GameItem item) =>
        new(item.identifier, item.unitCount, item.shape.width, item.shape.height,
            item.IsTag(ModuleHelper.MODULE_STUCK_TAG));

    internal static CellAssemblySnapshot Cell(GameItem host)
    {
        var component = CellContentWindow.Component(host);
        return new CellAssemblySnapshot(Item(host), component == null ? null : Item(component),
            CellContentWindow.IsMounted(host), SaveManager.IsDecoding,
            storageState: CellContentWindow.StorageState(host));
    }
}
