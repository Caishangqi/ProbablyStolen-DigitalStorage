using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>The back key closes mod windows in focus order; focus changes keep that order current.</summary>
internal static class WindowBackPatches
{
    internal static GamePatchGroup Group() => new(GameFeature.WindowBack, GamePatchTier.Enhancement,
        new GamePatch("StoreUIManager.CloseTopmostEscapeTarget",
            () => AccessTools.Method(typeof(StoreUIManager), "CloseTopmostEscapeTarget"),
            prefix: AccessTools.Method(typeof(WindowBackPatches), nameof(EscapePrefix))),
        new GamePatch("UIFamilyLayer.BringPanelsToFront",
            () => AccessTools.Method(typeof(UIFamilyLayer), nameof(UIFamilyLayer.BringPanelsToFront)),
            postfix: AccessTools.Method(typeof(WindowBackPatches), nameof(PanelsRaisedPostfix))),
        new GamePatch("UIFamilyLayer.BringWindowsToFront",
            () => AccessTools.Method(typeof(UIFamilyLayer), nameof(UIFamilyLayer.BringWindowsToFront)),
            postfix: AccessTools.Method(typeof(WindowBackPatches), nameof(WindowsRaisedPostfix))));

    private static bool EscapePrefix(StoreUIManager __instance, ref bool __result)
    {
        if (!WindowBackBridge.Dispatch(__instance)) return true;
        __result = true;
        return false;
    }

    private static void PanelsRaisedPostfix() => WindowBackBridge.PanelsRaised();

    private static void WindowsRaisedPostfix(PixelWindow focused) => WindowBackBridge.WindowsRaised(focused);
}
