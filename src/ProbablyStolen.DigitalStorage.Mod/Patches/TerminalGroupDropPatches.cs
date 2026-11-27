using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Makes storing the last step of a native group drop onto a terminal item, and colours the terminal while a group
/// hovers over it. The native drop runs unchanged; the postfix stores only what it left in place. The router reads the
/// group's private selection, offsets and hover target, so those fields belong to the group.
/// </summary>
/// <remarks>
/// Harmony keeps one <c>__state</c> per patch class on a method. <see cref="MachineBayGroupDropPatch"/> also patches
/// <c>EndGroupDrag</c> with its own state, so it stays a separate class. This group is applied first, so its prefix runs
/// first.
/// </remarks>
internal static class TerminalGroupDropPatches
{
    internal static GamePatchGroup Group() => new GamePatchGroup(GameFeature.TerminalGroupDrop, GamePatchTier.Core,
        new GamePatch("ItemMultiSelectHandler.EndGroupDrag",
            () => AccessTools.Method(typeof(ItemMultiSelectHandler), "EndGroupDrag"),
            prefix: AccessTools.Method(typeof(TerminalGroupDropPatches), nameof(EndGroupDragPrefix)),
            postfix: AccessTools.Method(typeof(TerminalGroupDropPatches), nameof(EndGroupDragPostfix)),
            finalizer: AccessTools.Method(typeof(TerminalGroupDropPatches), nameof(EndGroupDragFinalizer))),
        new GamePatch("ItemMultiSelectHandler.UpdateDropTarget",
            () => AccessTools.Method(typeof(ItemMultiSelectHandler), "UpdateDropTarget"),
            postfix: AccessTools.Method(typeof(TerminalGroupDropPatches), nameof(UpdateDropTargetPostfix))))
        .Requires("ItemMultiSelectHandler.selectedItems",
            () => AccessTools.Field(typeof(ItemMultiSelectHandler), "selectedItems")?.FieldType == typeof(List<GameItemElement>))
        .Requires("ItemMultiSelectHandler.dragOffsets",
            () => AccessTools.Field(typeof(ItemMultiSelectHandler), "dragOffsets")?.FieldType ==
                typeof(Dictionary<GameItemElement, Vector2>))
        .Requires("ItemMultiSelectHandler.hoverItem",
            () => AccessTools.Field(typeof(ItemMultiSelectHandler), "hoverItem")?.FieldType == typeof(GameItem))
        .Requires("ItemMultiSelectHandler.hoverHighlightNodes",
            () => AccessTools.Field(typeof(ItemMultiSelectHandler), "hoverHighlightNodes")?.FieldType ==
                typeof(List<TreeNodeRender>));

    private static void EndGroupDragPrefix(ItemMultiSelectHandler __instance, out object? __state) =>
        __state = TerminalItemDropRouter.BeginGroupDrop(__instance);

    private static void EndGroupDragPostfix(object? __state) => TerminalItemDropRouter.CompleteDrop(__state);

    private static Exception? EndGroupDragFinalizer(object? __state, Exception? __exception)
    {
        if (__exception != null && __state is IDisposable groupScope) groupScope.Dispose();
        return __exception;
    }

    private static void UpdateDropTargetPostfix(ItemMultiSelectHandler __instance) =>
        TerminalItemDropRouter.AfterGroupHover(__instance);
}
