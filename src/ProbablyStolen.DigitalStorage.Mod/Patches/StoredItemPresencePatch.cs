using System.Collections.Generic;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Leaves items stored in a network out of <c>PlayerStore.FindAllItem</c>, which the nightly cycle, hoppers, fridges,
/// alarms, current converters and ownership checks use to find the player's items. See
/// <see cref="StoredItemPresence"/>.
/// </summary>
internal static class StoredItemPresencePatch
{
    internal static GamePatchGroup Group() => new(GameFeature.StoredPresence, GamePatchTier.Integrity,
        new GamePatch("PlayerStore.FindAllItem", () => AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.FindAllItem)),
            postfix: AccessTools.Method(typeof(StoredItemPresencePatch), nameof(Postfix))));

    private static void Postfix(List<GameItem> __result) => StoredItemPresence.HideStored(__result);
}
