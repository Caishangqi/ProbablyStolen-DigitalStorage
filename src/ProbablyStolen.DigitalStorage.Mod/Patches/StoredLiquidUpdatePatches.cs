using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

// EmporiumEntry's one-second item scan reaches cell pages independently of PlayerStore.FindAllItem.
[HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleAmbientTempDrop))]
internal static class StoredLiquidTemperaturePatch
{
    private static bool Prefix(GameItem container) => StoredItemPresence.ShouldUpdateLiquid(container);
}

[HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleTemperatureReaction))]
internal static class StoredLiquidReactionPatch
{
    private static bool Prefix(GameItem container) => StoredItemPresence.ShouldUpdateLiquid(container);
}
