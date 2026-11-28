using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Api;

/// <summary>Public API exposed as <c>content.v2</c>; registry values are read-only after bootstrap.</summary>
public interface IDigitalStorageContentApi
{
    /// <summary>Gets API major version.</summary>
    int ApiMajorVersion { get; }
    /// <summary>Gets additive content API minor version.</summary>
    int ApiMinorVersion { get; }
    /// <summary>Gets the owner scoped registration entry point.</summary>
    IDigitalStorageContentBootstrap Bootstrap { get; }
    /// <summary>Gets the frozen module definition registry.</summary>
    IReadOnlyRegistry<ModuleBayDefinition> ModuleBays { get; }
    /// <summary>Gets the frozen item binding registry.</summary>
    IReadOnlyRegistry<ModuleBayBinding> ModuleBayBindings { get; }
    /// <summary>Gets the frozen builtin customer registry, <see cref="DigitalStorageRegistries.Customer"/>.</summary>
    IReadOnlyRegistry<CustomerDefinition> Customers { get; }
    /// <summary>Looks up a frozen custom registry by identifier.</summary>
    IReadOnlyRegistry<T> GetRegistry<T>(Identifier id);
    /// <summary>Gets the module-bay operations API.</summary>
    IModuleBayApi ModuleBaysApi { get; }
}

/// <summary>Bootstrap operations accepted until the assets-loaded phase completes.</summary>
public interface IDigitalStorageContentBootstrap
{
    /// <summary>Stages one atomic content contribution owned by <paramref name="sourceModId"/>.</summary>
    void RegisterBatch(string sourceModId, Action<IContentRegistrationBatch> register);
    /// <summary>Gets whether bootstrap is frozen.</summary>
    bool IsFrozen { get; }
}

/// <summary>Provides access to the module inventory attached to a particular native item instance.</summary>
public interface IModuleBayProvider
{
    /// <summary>Gets this provider's namespaced registry identifier.</summary>
    Identifier Id { get; }
    /// <summary>Checks whether the item is supported and returns its actual module inventory.</summary>
    bool TryGetInventory(GameItem item, out GameGridInventory inventory);
    /// <summary>Reports whether the inventory can safely be read or changed.</summary>
    bool IsAvailable(GameItem item, out string? refusalReason);
}

/// <summary>Immutable view of one item's managed module bay.</summary>
public sealed class ModuleBayState
{
    /// <summary>Creates an immutable state snapshot.</summary>
    public ModuleBayState(bool available, int stage, ModuleBayDefinition? definition, string? refusalReason = null,
        int? maximumStage = null)
    { Available = available; Stage = stage; Definition = definition; RefusalReason = refusalReason; MaximumStage = maximumStage ?? Math.Max(0, (definition?.Stages.Count ?? 1) - 1); }
    /// <summary>Gets the effective stage limit of this item binding.</summary>
    public int MaximumStage { get; }
    /// <summary>Gets whether the native inventory and registered definition are usable.</summary>
    public bool Available { get; }
    /// <summary>Gets the observed stage.</summary>
    public int Stage { get; }
    /// <summary>Gets the definition, or null when the item is not bound.</summary>
    public ModuleBayDefinition? Definition { get; }
    /// <summary>Gets an optional unavailable reason.</summary>
    public string? RefusalReason { get; }
}

/// <summary>Instance-level module bay operations. Layout expansion does not consume a kit.</summary>
public interface IModuleBayApi
{
    /// <summary>Initializes a newly created or restored item idempotently.</summary>
    ModuleBayState Initialize(GameItem item);
    /// <summary>Queries the state of this native item instance.</summary>
    ModuleBayState GetState(GameItem item);
    /// <summary>Checks whether the item can advance one stage.</summary>
    ModuleBayChangeResult CanExpand(GameItem item);
    /// <summary>Applies the next layout and stage without consuming a kit.</summary>
    ModuleBayChangeResult Expand(GameItem item);
    /// <summary>Consumes or applies a kit through the native backend after validating this item instance.</summary>
    ModuleBayChangeResult TryUseKit(GameItem machine, GameItem kit);
    /// <summary>Subscribes to successful layout changes; disposing the token removes the listener.</summary>
    IDisposable SubscribeLayoutChanged(Action<GameItem, ModuleBayState> listener);
}

/// <summary>Names and version of the additive public content service.</summary>
public static class DigitalStorageContentKeys
{
    /// <summary>Gets the mod-loader service key.</summary>
    public const string ContentServiceKey = "content.v2";
    /// <summary>Gets the content API minor version.</summary>
    public const int ContentApiMinor = 0;
}

/// <summary>Allows a dependent mod to contribute C# content during its enable lifecycle.</summary>
public interface IDigitalStorageContentRegistration
{
    /// <summary>Registers a contribution under the dependent mod's manifest identifier.</summary>
    void Register(IDigitalStorageContentBootstrap bootstrap);
}
