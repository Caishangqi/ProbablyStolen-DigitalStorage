using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>Prefers the first eligible bay among ties in registered machine grids.</summary>
internal static class ForwardFillGridPatch
{
    internal static GamePatchGroup Group() => new(GameFeature.ForwardFill, GamePatchTier.Enhancement,
        new GamePatch("GridPixelElement.TryFindOneValidInventorySlot",
            () => AccessTools.Method(typeof(GridPixelElement), nameof(GridPixelElement.TryFindOneValidInventorySlot)),
            prefix: AccessTools.Method(typeof(ForwardFillGridPatch), nameof(Prefix))));

    private static bool Prefix(GridPixelElement __instance, GameItem item, ref SlotMarker? __result) =>
        !ForwardFillGrids.TryFind(__instance, item, out __result);
}
