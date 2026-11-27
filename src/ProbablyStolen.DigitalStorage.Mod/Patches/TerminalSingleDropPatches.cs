using HarmonyLib;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Makes storing the last step of a single native drop onto a terminal item. The native drop runs unchanged; the
/// postfix stores only what it left in place. The hover postfix colours the terminal as a drop target. The router
/// reads the drag's private amount, hover target and highlight, so those fields belong to the group.
/// </summary>
internal static class TerminalSingleDropPatches
{
    internal static GamePatchGroup Group() => new GamePatchGroup(GameFeature.TerminalSingleDrop, GamePatchTier.Core,
        new GamePatch("ItemMouseDragHandler.EndDrag", () => AccessTools.Method(typeof(ItemMouseDragHandler), "EndDrag"),
            prefix: AccessTools.Method(typeof(TerminalSingleDropPatches), nameof(EndDragPrefix)),
            postfix: AccessTools.Method(typeof(TerminalSingleDropPatches), nameof(EndDragPostfix))),
        new GamePatch("ItemMouseDragHandler.Drag",
            () => AccessTools.Method(typeof(ItemMouseDragHandler), "Drag", new[] { typeof(Vector3) }),
            postfix: AccessTools.Method(typeof(TerminalSingleDropPatches), nameof(DragPostfix))))
        .Requires("ItemMouseDragHandler.currentRemove",
            () => AccessTools.Field(typeof(ItemMouseDragHandler), "currentRemove")?.FieldType == typeof(int))
        .Requires("ItemMouseDragHandler.lastItem",
            () => AccessTools.Field(typeof(ItemMouseDragHandler), "lastItem")?.FieldType == typeof(GameItem))
        .Requires("ItemMouseDragHandler.lastInventory",
            () => AccessTools.Field(typeof(ItemMouseDragHandler), "lastInventory")?.FieldType == typeof(GameInventory))
        .Requires("ItemMouseDragHandler.lastSlot",
            () => AccessTools.Field(typeof(ItemMouseDragHandler), "lastSlot")?.FieldType == typeof(SlotMarker))
        .Requires("ItemMouseDragHandler.highlightManager",
            () => AccessTools.Field(typeof(ItemMouseDragHandler), "highlightManager")?.FieldType == typeof(HighlightManager));

    private static void EndDragPrefix(ItemMouseDragHandler __instance, bool cancelled, out object? __state) =>
        __state = TerminalItemDropRouter.BeginSingleDrop(__instance, cancelled);

    private static void EndDragPostfix(object? __state) => TerminalItemDropRouter.CompleteDrop(__state);

    private static void DragPostfix(ItemMouseDragHandler __instance) => TerminalItemDropRouter.AfterSingleHover(__instance);
}
