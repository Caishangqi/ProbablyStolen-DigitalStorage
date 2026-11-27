using System;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

internal static class NetworkPowerTooltipPatch
{
    internal static GamePatchGroup Group() => new(GameFeature.NetworkPowerTooltip, GamePatchTier.Enhancement,
        new GamePatch("GameItemElement.GetTooltipBasic",
            () => AccessTools.Method(typeof(GameItemElement), "GetTooltipBasic", Type.EmptyTypes),
            prefix: AccessTools.Method(typeof(NetworkPowerTooltipPatch), nameof(Prefix)),
            finalizer: AccessTools.Method(typeof(NetworkPowerTooltipPatch), nameof(Finalizer))));

    // Item Details reads the power tag in its prefix on the inner machinery tooltip method.
    internal static void Prefix(GameItemElement __instance, out IDisposable? __state) =>
        __state = NetworkPowerTooltipBridge.Begin(__instance);

    internal static Exception? Finalizer(Exception? __exception, IDisposable? __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
