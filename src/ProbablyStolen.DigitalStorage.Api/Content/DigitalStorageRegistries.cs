using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Api.Content;

/// <summary>
/// Keys of the builtin registries. Contribute with
/// <c>DeferredRegister&lt;T&gt;.Create(batch.Registries, DigitalStorageRegistries.Customer, "my_mod")</c>; the
/// registry puts its entries into the game when the registries freeze.
/// </summary>
public static class DigitalStorageRegistries
{
    /// <summary>Gets the store customer registry; entries are added to the game's customer catalog.</summary>
    public static readonly ResourceKey<Registry<CustomerDefinition>> Customer =
        ResourceKey.CreateRegistryKey<CustomerDefinition>(Identifier.Parse(CustomerIdentity.RegistryId));

    /// <summary>Gets the module bay definition registry; an entry id must equal its definition id.</summary>
    public static readonly ResourceKey<Registry<ModuleBayDefinition>> ModuleBayDefinition =
        ResourceKey.CreateRegistryKey<ModuleBayDefinition>(Identifier.Parse("digital_storage:module_bay_definition"));

    /// <summary>Gets the module bay binding registry; each native item has at most one binding.</summary>
    public static readonly ResourceKey<Registry<ModuleBayBinding>> ModuleBayBinding =
        ResourceKey.CreateRegistryKey<ModuleBayBinding>(Identifier.Parse("digital_storage:module_bay_binding"));

    /// <summary>Gets the module bay provider registry; an entry id must equal its provider id.</summary>
    public static readonly ResourceKey<Registry<IModuleBayProvider>> ModuleBayProvider =
        ResourceKey.CreateRegistryKey<IModuleBayProvider>(Identifier.Parse("digital_storage:module_bay_provider"));
}
