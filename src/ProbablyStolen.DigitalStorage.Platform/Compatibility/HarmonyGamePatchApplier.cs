using System;
using System.Reflection;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Platform.Compatibility;

/// <summary>
/// Applies patches with a Harmony instance under this mod's loader id (<c>mod.</c> + manifest id). The loader removes
/// every patch under that id when it stops the mod or when <c>Init</c> throws, so these patches leave with it.
/// </summary>
public sealed class HarmonyGamePatchApplier : IGamePatchApplier
{
    private readonly Harmony _harmony;

    /// <summary>Creates the applier for the mod with this manifest id.</summary>
    public HarmonyGamePatchApplier(string modId) =>
        _harmony = new Harmony("mod." + (string.IsNullOrEmpty(modId) ? throw new ArgumentException("A mod id is required.", nameof(modId)) : modId));

    /// <inheritdoc />
    public void Apply(MethodBase target, GamePatch patch) =>
        _harmony.Patch(target, Wrap(patch.Prefix), Wrap(patch.Postfix), Wrap(patch.Transpiler), Wrap(patch.Finalizer));

    /// <inheritdoc />
    public void Remove(MethodBase target, GamePatch patch)
    {
        foreach (var method in new[] { patch.Prefix, patch.Postfix, patch.Transpiler, patch.Finalizer })
        {
            if (method != null) _harmony.Unpatch(target, method);
        }
    }

    private static HarmonyMethod? Wrap(MethodInfo? method) => method == null ? null : new HarmonyMethod(method);
}
