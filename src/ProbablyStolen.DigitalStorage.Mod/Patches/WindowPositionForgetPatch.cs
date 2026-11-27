using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

internal static class WindowPositionForgetPatch
{
    internal static GamePatchGroup Group() => new(GameFeature.WindowPositionForget, GamePatchTier.Enhancement,
        new GamePatch("GameItemElement.Destroy", () => AccessTools.Method(typeof(GameItemElement), nameof(GameItemElement.Destroy)),
            postfix: AccessTools.Method(typeof(WindowPositionForgetPatch), nameof(Postfix))));

    private static void Postfix(GameItemElement __instance) => WindowPositionEvents.ItemDestroyed(__instance);
}
