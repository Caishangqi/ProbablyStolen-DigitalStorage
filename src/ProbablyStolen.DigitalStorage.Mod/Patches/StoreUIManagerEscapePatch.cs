using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

[HarmonyPatch(typeof(StoreUIManager), "CloseTopmostEscapeTarget")]
internal static class StoreUIManagerEscapePatch
{
    private static bool Prefix(StoreUIManager __instance, ref bool __result)
    {
        if (!WindowBackBridge.Dispatch(__instance)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(UIFamilyLayer), nameof(UIFamilyLayer.BringPanelsToFront))]
internal static class PaperFocusLayerPatch
{
    private static void Postfix() => WindowBackBridge.PanelsRaised();
}

[HarmonyPatch(typeof(UIFamilyLayer), nameof(UIFamilyLayer.BringWindowsToFront))]
internal static class WindowFocusLayerPatch
{
    private static void Postfix(PixelWindow focused) => WindowBackBridge.WindowsRaised(focused);
}
