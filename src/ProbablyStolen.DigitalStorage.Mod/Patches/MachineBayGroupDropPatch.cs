using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Spreads a group dropped onto one empty drive or IO Port bay over the sibling bays; the native drop runs unchanged
/// and the postfix only places what it left behind (<see cref="MachineBayGroupDrop"/>).
/// </summary>
internal static class MachineBayGroupDropPatch
{
    internal static GamePatchGroup Group() => new(GameFeature.MachineBayGroupDrop, GamePatchTier.Enhancement,
        new GamePatch("ItemMultiSelectHandler.EndGroupDrag",
            () => AccessTools.Method(typeof(ItemMultiSelectHandler), "EndGroupDrag"),
            prefix: AccessTools.Method(typeof(MachineBayGroupDropPatch), nameof(Prefix)),
            postfix: AccessTools.Method(typeof(MachineBayGroupDropPatch), nameof(Postfix))));

    private static void Prefix(ItemMultiSelectHandler __instance, out object? __state) =>
        __state = MachineBayGroupDrop.Begin(__instance);

    private static void Postfix(object? __state) => MachineBayGroupDrop.Complete(__state);
}
