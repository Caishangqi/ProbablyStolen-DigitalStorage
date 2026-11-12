using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.Destroy))]
internal static class WindowPositionForgetPatch
{
    private static void Postfix(GameItemElement __instance) => WindowPositionEvents.ItemDestroyed(__instance);
}
