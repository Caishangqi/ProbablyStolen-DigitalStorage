using System;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Platform.Integration;

/// <summary>The game loader behind <see cref="IIntegrationEnvironment"/>; only reads loader state.</summary>
internal sealed class NativeIntegrationEnvironment : IIntegrationEnvironment
{
    /// <inheritdoc />
    public bool IsModEnabled(string modId) => ModLoader.IsModEnabled(modId);

    /// <inheritdoc />
    public bool IsModRunning(string modId) => ModHelper.IsModRunning(modId);

    /// <inheritdoc />
    public string? ModVersion(string modId) => ModLoader.Mods.Find(mod => mod.Manifest.ID == modId)?.Manifest.ModVersion;
}

/// <summary>
/// Applies plugin patches with the Harmony instance the loader holds for this mod (id <c>mod.digital_storage</c>), so
/// the loader removes them if it stops the mod. Removal unpatches only the plugin's own patch methods.
/// </summary>
/// <remarks>
/// The loader records the instance only after this mod's <c>OnEnable</c> returned (<c>ModLoader.LoadModDll</c>), so it
/// is looked up when a patch is applied, which the host does after every mod is loaded.
/// </remarks>
internal sealed class HarmonyIntegrationPatcher : IIntegrationPatcher
{
    private readonly string _ownModId;

    internal HarmonyIntegrationPatcher(string ownModId) =>
        _ownModId = string.IsNullOrEmpty(ownModId) ? throw new ArgumentException("A mod id is required.", nameof(ownModId)) : ownModId;

    private Harmony Instance => ModLoader.Mods.Find(mod => mod.Manifest.ID == _ownModId)?.HarmonyInstance
        ?? throw new InvalidOperationException($"The loader holds no Harmony instance for {_ownModId}.");

    /// <inheritdoc />
    public void Apply(PatchRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        Instance.Patch(request.Target, Wrap(request.Prefix), Wrap(request.Postfix), Wrap(request.Transpiler),
            Wrap(request.Finalizer));
    }

    /// <inheritdoc />
    public void Remove(PatchRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var harmony = Instance;
        foreach (var method in new[] { request.Prefix, request.Postfix, request.Transpiler, request.Finalizer })
        {
            if (method != null) harmony.Unpatch(request.Target, method);
        }
    }

    private static HarmonyMethod? Wrap(System.Reflection.MethodInfo? method) => method == null ? null : new HarmonyMethod(method);
}
