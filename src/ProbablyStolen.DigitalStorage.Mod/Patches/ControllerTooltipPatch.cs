using System;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

[HarmonyPatch(typeof(GameItemElement), "GetTooltipBasic", new Type[] { })]
internal static class ControllerTooltipPatch
{
    internal static void Prefix(GameItemElement __instance, out IDisposable? __state) =>
        __state = ControllerTooltipBridge.Begin(__instance);

    internal static Exception? Finalizer(Exception? __exception, IDisposable? __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
