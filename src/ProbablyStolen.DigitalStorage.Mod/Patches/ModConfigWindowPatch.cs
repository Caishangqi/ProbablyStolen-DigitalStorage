using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>Refreshes the own-mod pinyin mode labels when the native settings window is built.</summary>
[HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
internal static class ModConfigWindowPatch
{
    private static void Postfix(LoadedMod mod, CustomUIWindow __result) => PinyinModeDropdown.Localize(mod, __result);
}
