using ProbablyStolen.DigitalStorage.Api.Networking;
using ProbablyStolen.DigitalStorage.Api.Storage;

namespace ProbablyStolen.DigitalStorage.Api;

/// <summary>
/// Integration plugins receive the public API in their lifecycle callbacks. Other mods that list
/// <c>digital_storage</c> in <c>&lt;Prerequisites&gt;</c> get it with
/// <c>ModHelper.Lookup&lt;IDigitalStorageRuntime&gt;("digital_storage", RuntimeServiceKey)</c>.
/// </summary>
public interface IDigitalStorageRuntime
{
    /// <summary>Gets the network lookup.</summary>
    IGridHelper Grids { get; }

    /// <summary>Gets the scoped access for other mods' item scans.</summary>
    IExternalAccess ExternalAccess { get; }
}

/// <summary>Keys under which the runtime is exposed.</summary>
public static class DigitalStorageRuntimeKeys
{
    /// <summary>Gets the mod id that exposes the runtime.</summary>
    public const string ModId = "digital_storage";

    /// <summary>Gets the <c>ModHelper.Expose</c> key of the runtime for API major version 1.</summary>
    public const string RuntimeServiceKey = "api.v1";
}
