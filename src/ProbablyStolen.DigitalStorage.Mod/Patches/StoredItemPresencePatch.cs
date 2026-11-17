using System.Collections.Generic;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Leaves items stored in a network out of <c>PlayerStore.FindAllItem</c>, which the nightly cycle, hoppers, fridges,
/// alarms, current converters and ownership checks use to find the player's items. See
/// <see cref="StoredItemPresence"/>.
/// </summary>
[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.FindAllItem))]
internal static class StoredItemPresencePatch
{
    private static void Postfix(List<GameItem> __result) => StoredItemPresence.HideStored(__result);
}
