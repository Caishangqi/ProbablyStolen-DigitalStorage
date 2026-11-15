using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>Accesses the ordinary native machine module inventory.</summary>
internal sealed class NativeStandardModuleBayProvider : IModuleBayProvider
{
    public Identifier Id => Identifier.Parse("digital_storage:native_standard");
    public bool TryGetInventory(GameItem item, out GameGridInventory inventory)
    {
        inventory = null!;
        if (item == null || !item.IsTag("STANDARD_MACHINE_TAG")) return false;
        inventory = (MachineHelper.GetBodyStorage(item, 1) as GameGridInventory)!;
        return inventory != null;
    }
    public bool IsAvailable(GameItem item, out string? refusalReason)
    {
        var available = TryGetInventory(item, out _);
        refusalReason = available ? null : "standard machine module inventory is unavailable";
        return available;
    }
}
