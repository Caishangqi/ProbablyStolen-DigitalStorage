using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Reports changes to item features, tags and lore to <see cref="StoredItemChanges"/>. Without this group, a change
/// to a stored item is still found when the root is about to leave storage, at the cost of one failed transfer.
/// </summary>
internal static class StoredItemChangePatches
{
    internal static GamePatchGroup Group() => new(GameFeature.StoredItemChanges, GamePatchTier.Enhancement,
        new GamePatch("GameItem.AddItemFeature",
            () => AccessTools.Method(typeof(GameItem), nameof(GameItem.AddItemFeature), new[] { typeof(ItemFeature) }),
            prefix: Method(nameof(FeaturesBefore)), postfix: Method(nameof(FeaturesAfter))),
        new GamePatch("GameItem.RemoveItemFeature",
            () => AccessTools.Method(typeof(GameItem), nameof(GameItem.RemoveItemFeature), new[] { typeof(ItemFeature) }),
            prefix: Method(nameof(FeaturesBefore)), postfix: Method(nameof(FeaturesAfter))),
        new GamePatch("GameItem.RemoveItemFeatureByID",
            () => AccessTools.Method(typeof(GameItem), nameof(GameItem.RemoveItemFeatureByID), new[] { typeof(string) }),
            prefix: Method(nameof(FeaturesBefore)), postfix: Method(nameof(FeaturesAfter))),
        new GamePatch("GameItem.RemoveTemporaryClientFeature",
            () => AccessTools.Method(typeof(GameItem), nameof(GameItem.RemoveTemporaryClientFeature), System.Type.EmptyTypes),
            prefix: Method(nameof(FeaturesBefore)), postfix: Method(nameof(FeaturesAfter))),
        new GamePatch("GameItem.ModifyTag",
            () => AccessTools.Method(typeof(GameItem), nameof(GameItem.ModifyTag),
                new[] { typeof(string), typeof(System.Action<TagState>), typeof(bool) }),
            postfix: Method(nameof(TagModified))),
        new GamePatch("GameItem.EnableTag",
            () => AccessTools.Method(typeof(GameItem), nameof(GameItem.EnableTag), new[] { typeof(string), typeof(bool) }),
            postfix: Method(nameof(TagToggled))),
        new GamePatch("GameItem.DisableTag",
            () => AccessTools.Method(typeof(GameItem), nameof(GameItem.DisableTag), new[] { typeof(string), typeof(bool) }),
            postfix: Method(nameof(TagToggled))),
        new GamePatch("GameItem.shortDescription",
            () => AccessTools.PropertySetter(typeof(GameItem), nameof(GameItem.shortDescription)),
            prefix: Method(nameof(LoreBefore)), postfix: Method(nameof(LoreAfter))),
        new GamePatch("GameItem.flavorText",
            () => AccessTools.PropertySetter(typeof(GameItem), nameof(GameItem.flavorText)),
            prefix: Method(nameof(FlavorBefore)), postfix: Method(nameof(FlavorAfter))));

    private static System.Reflection.MethodInfo Method(string name) =>
        AccessTools.Method(typeof(StoredItemChangePatches), name);

    private static void FeaturesBefore(GameItem __instance, out int __state)
    {
        __state = __instance.itemFeatures?.Count ?? -1;
        StoredItemChanges.Changing(__instance);
    }

    private static void FeaturesAfter(GameItem __instance, int __state)
    {
        if ((__instance.itemFeatures?.Count ?? -1) != __state) StoredItemChanges.Changed(__instance, restorable: true);
    }

    // The action can change anything in the tag, so every call counts as a change.
    private static void TagModified(GameItem __instance) => StoredItemChanges.Changed(__instance, restorable: false);

    private static void TagToggled(GameItem __instance, bool __result)
    {
        if (__result) StoredItemChanges.Changed(__instance, restorable: false);
    }

    private static void LoreBefore(GameItem __instance, out string __state)
    {
        __state = __instance.shortDescription;
        StoredItemChanges.Changing(__instance);
    }

    private static void LoreAfter(GameItem __instance, string __state)
    {
        if (!string.Equals(__instance.shortDescription, __state, System.StringComparison.Ordinal))
            StoredItemChanges.Changed(__instance, restorable: true);
    }

    private static void FlavorBefore(GameItem __instance, out string __state)
    {
        __state = __instance.flavorText;
        StoredItemChanges.Changing(__instance);
    }

    private static void FlavorAfter(GameItem __instance, string __state)
    {
        if (!string.Equals(__instance.flavorText, __state, System.StringComparison.Ordinal))
            StoredItemChanges.Changed(__instance, restorable: true);
    }
}
