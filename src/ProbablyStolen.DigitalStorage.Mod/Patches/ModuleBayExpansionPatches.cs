using System;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>Wraps every newly constructed native kit before it is used or decoded.</summary>
[HarmonyPatch(typeof(ModuleDirectory), "ModuleBayExpansionKit")]
internal static class ModuleBayExpansionKitPatch
{
    private static void Postfix(GameItem __result) => ModuleBayExpansionHooks.ExtendKit(__result);
}

/// <summary>Uses the bound stage limit for native expansion checks.</summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.CanExpand))]
internal static class CanExpandModulePatch
{
    private static bool Prefix(GameItem item, ref bool __result)
    {
        if (!ModuleBayExpansionHooks.IsManaged(item)) return true;
        __result = !ModuleBayExpansionHooks.RejectsThread && ModuleBayExpansionHooks.CanExpand(item);
        return false;
    }
}

/// <summary>Changes only the bound layout and stage, retaining native callers' tool ownership.</summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.ExpandModule))]
internal static class ExpandModulePatch
{
    private static bool Prefix(GameItem item)
    {
        if (!ModuleBayExpansionHooks.IsManaged(item)) return true;
        if (!ModuleBayExpansionHooks.RejectsThread) ModuleBayExpansionHooks.Expand(item);
        return false;
    }
}

/// <summary>Checks the final native targeting result after combined rules have run.</summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.MayTarget))]
internal static class KitMayTargetPatch
{
    private static void Postfix(GameItem __instance, GameItem targetItem, ref bool __result)
    {
        if (__result && ModuleBayExpansionHooks.RefusesTarget(__instance, targetItem)) __result = false;
    }
}

/// <summary>Checks the final native permission before the tool action.</summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.CanTarget))]
internal static class KitCanTargetPatch
{
    private static void Postfix(GameItem __instance, GameItem targetItem, ref bool __result)
    {
        if (__result && ModuleBayExpansionHooks.RefusesTarget(__instance, targetItem)) __result = false;
    }
}

/// <summary>Restores registered stages without applying the native two-stage pattern switch.</summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.LoadUpgrade))]
internal static class LoadUpgradePatch
{
    private static bool Prefix(GameItem item)
    {
        if (!ModuleBayExpansionHooks.IsManaged(item)) return true;
        if (!ModuleBayExpansionHooks.RejectsThread) ModuleBayExpansionHooks.Initialize(item);
        return false;
    }
}

/// <summary>Initializes bound products created by native or external directories.</summary>
[HarmonyPatch(typeof(DirectoryMaster), nameof(DirectoryMaster.Item), new Type[] { typeof(string), typeof(bool) })]
internal static class ModuleBayItemCreatedPatch
{
    private static void Postfix(GameItem __result) => ModuleBayExpansionHooks.Initialize(__result);
}
