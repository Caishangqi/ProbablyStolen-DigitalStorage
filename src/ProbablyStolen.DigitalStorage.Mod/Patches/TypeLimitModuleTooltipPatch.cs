using System;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

internal static class TypeLimitModuleTooltipPatch
{
    internal static GamePatchGroup Group() => new(GameFeature.TypeLimitTooltip, GamePatchTier.Enhancement,
        new GamePatch("ModuleHelper.CreateModuleTooltip",
            () => AccessTools.Method(typeof(ModuleHelper), nameof(ModuleHelper.CreateModuleTooltip),
                new[] { typeof(RichTextBuilder), typeof(GameItem) }),
            prefix: AccessTools.Method(typeof(TypeLimitModuleTooltipPatch), nameof(Prefix)),
            finalizer: AccessTools.Method(typeof(TypeLimitModuleTooltipPatch), nameof(Finalizer))));

    internal static void Prefix(GameItem item, out IDisposable? __state) =>
        __state = TypeLimitModuleTooltipBridge.Begin(item);

    internal static Exception? Finalizer(Exception? __exception, IDisposable? __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
