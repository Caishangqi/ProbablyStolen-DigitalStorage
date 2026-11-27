using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

// EmporiumEntry's one-second item scan reaches cell pages independently of PlayerStore.FindAllItem.
internal static class StoredLiquidUpdatePatches
{
    internal static GamePatchGroup Group() => new(GameFeature.StoredLiquid, GamePatchTier.Integrity,
        new GamePatch("ChemicalHelper.HandleAmbientTempDrop",
            () => AccessTools.Method(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleAmbientTempDrop)),
            prefix: AccessTools.Method(typeof(StoredLiquidUpdatePatches), nameof(Prefix))),
        new GamePatch("ChemicalHelper.HandleTemperatureReaction",
            () => AccessTools.Method(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleTemperatureReaction)),
            prefix: AccessTools.Method(typeof(StoredLiquidUpdatePatches), nameof(Prefix))));

    private static bool Prefix(GameItem container) => StoredItemPresence.ShouldUpdateLiquid(container);
}
