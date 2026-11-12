using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

[HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
internal static class ModConfigWindowPatch
{
    private static void Postfix(LoadedMod mod, CustomUIWindow __result)
    {
        PinyinModeDropdown.Localize(mod, __result);
        TerminalViewDropdowns.Localize(mod, __result);
    }
}
