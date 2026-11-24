using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// The builtin module bay content: the cell and native standard providers, the terminal and vanilla standard
/// expansion definitions, and the bindings of this mod's machines, cells and housing to them.
/// </summary>
internal static class BuiltinModuleBays
{
    internal static void Register(RegistryRegistration registries)
    {
        var providers = DeferredRegister<IModuleBayProvider>.Create(registries, DigitalStorageRegistries.ModuleBayProvider, "digital_storage");
        providers.Register("native_standard", () => new NativeStandardModuleBayProvider());
        providers.Register("cell", () => new CellModuleBayProvider());
        var definitions = DeferredRegister<ModuleBayDefinition>.Create(registries, DigitalStorageRegistries.ModuleBayDefinition, "digital_storage");
        definitions.Register("terminal", id => new ModuleBayDefinition(id.ToString(), new[]
        {
            new ModuleBayStage(0, "base", ModuleBayLayout.Pattern(new[] { "1000001", "0001000", "0011100", "0001000", "1000001" })),
            new ModuleBayStage(1, "expansion_01", ModuleBayLayout.Pattern(new[] { "1000001", "0000000", "0000000", "0000000", "1000001" })),
            new ModuleBayStage(2, "expansion_02", ModuleBayLayout.Pattern(new[] { "100000001", "000000000", "000000000", "000000000", "100000001" })),
            new ModuleBayStage(3, "expansion_03", ModuleBayLayout.Pattern(new[] { "100000001", "000000000", "000000000", "000000000", "000000000", "000000000", "100000001" })),
            new ModuleBayStage(4, "expansion_04", ModuleBayLayout.Pattern(new[] { "10000000001", "00000000000", "00000000000", "00000000000", "00000000000", "00000000000", "10000000001" })),
            new ModuleBayStage(5, "expansion_05", ModuleBayLayout.Pattern(new[] { "10000000001", "00000000000", "00000000000", "00000000000", "00000000000", "00000000000", "00000000000", "00000000000", "10000000001" })),
            new ModuleBayStage(6, "expansion_06", ModuleBayLayout.Pattern(new[] { "1000000000001", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "1000000000001" })),
            new ModuleBayStage(7, "expansion_07", ModuleBayLayout.Pattern(new[] { "1000000000001", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "0000000000000", "1000000000001" })),
            new ModuleBayStage(8, "expansion_08", ModuleBayLayout.Pattern(new[] { "100000000000001", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "100000000000001" })),
            new ModuleBayStage(9, "expansion_09", ModuleBayLayout.Pattern(new[] { "100000000000001", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "000000000000000", "100000000000001" })),
            new ModuleBayStage(10, "expansion_10", ModuleBayLayout.Pattern(new[] { "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000", "00000000000000000" })),
        }));
        definitions.Register("vanilla_standard", id => new ModuleBayDefinition(id.ToString(), new[]
        {
            new ModuleBayStage(0, "base", ModuleBayLayout.Pattern(new[] { "1000001", "0001000", "0011100", "0001000", "1000001" })),
            new ModuleBayStage(1, "expansion_01", ModuleBayLayout.Pattern(new[] { "1000001", "0000000", "0000000", "0000000", "1000001" })),
            new ModuleBayStage(2, "expansion_02", ModuleBayLayout.Pattern(new[] { "0000000", "0000000", "0000000", "0000000", "0000000" })),
        }));
        var bindings = DeferredRegister<ModuleBayBinding>.Create(registries, DigitalStorageRegistries.ModuleBayBinding, "digital_storage");
        bindings.Register("digital_storage.storage_terminal", () => new ModuleBayBinding("digital_storage.storage_terminal", "digital_storage:terminal", "digital_storage:native_standard", 10));
        bindings.Register("digital_storage.storage_drive", () => new ModuleBayBinding("digital_storage.storage_drive", "digital_storage:vanilla_standard", "digital_storage:native_standard", 2));
        bindings.Register("digital_storage.storage_network_controller", () => new ModuleBayBinding("digital_storage.storage_network_controller", "digital_storage:vanilla_standard", "digital_storage:native_standard", 2));
        bindings.Register(IoPortItem.ItemId, () => new ModuleBayBinding(IoPortItem.ItemId, "digital_storage:vanilla_standard", "digital_storage:native_standard", 2));
        bindings.Register("digital_storage.item_storage_cell_1k", () => new ModuleBayBinding("digital_storage.item_storage_cell_1k", "digital_storage:vanilla_standard", "digital_storage:cell", 0));
        bindings.Register("digital_storage.item_storage_cell_4k", () => new ModuleBayBinding("digital_storage.item_storage_cell_4k", "digital_storage:vanilla_standard", "digital_storage:cell", 0));
        bindings.Register("digital_storage.item_storage_cell_16k", () => new ModuleBayBinding("digital_storage.item_storage_cell_16k", "digital_storage:vanilla_standard", "digital_storage:cell", 1));
        bindings.Register("digital_storage.item_storage_cell_64k", () => new ModuleBayBinding("digital_storage.item_storage_cell_64k", "digital_storage:vanilla_standard", "digital_storage:cell", 1));
        bindings.Register("digital_storage.item_storage_cell_256k", () => new ModuleBayBinding("digital_storage.item_storage_cell_256k", "digital_storage:vanilla_standard", "digital_storage:cell", 2));
        bindings.Register("digital_storage.item_cell_housing", () => new ModuleBayBinding("digital_storage.item_cell_housing", "digital_storage:vanilla_standard", "digital_storage:cell", 2));
    }
}
