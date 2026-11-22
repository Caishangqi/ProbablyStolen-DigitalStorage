using System;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

[HarmonyPatch(typeof(ModuleHelper), nameof(ModuleHelper.CreateModuleTooltip), new[] { typeof(RichTextBuilder), typeof(GameItem) })]
internal static class TypeLimitModuleTooltipPatch
{
    internal static void Prefix(GameItem item, out IDisposable? __state) =>
        __state = TypeLimitModuleTooltipBridge.Begin(item);

    internal static Exception? Finalizer(Exception? __exception, IDisposable? __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
