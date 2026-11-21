using System;
using HarmonyLib;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Makes storing the last step of a native drop onto a terminal item. The native drop runs unchanged; the postfix
/// stores only what it left in place. Like <see cref="TerminalOpenPatch"/>, the patches live in the IMod assembly
/// that the loader patches and only call <see cref="TerminalItemDropRouter"/>.
/// </summary>
[HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
internal static class TerminalSingleDropPatch
{
    private static void Prefix(ItemMouseDragHandler __instance, bool cancelled, out object? __state) =>
        __state = TerminalItemDropRouter.BeginSingleDrop(__instance, cancelled);

    private static void Postfix(object? __state) => TerminalItemDropRouter.CompleteDrop(__state);
}

[HarmonyPatch(typeof(ItemMouseDragHandler), "Drag", typeof(Vector3))]
internal static class TerminalSingleHoverPatch
{
    private static void Postfix(ItemMouseDragHandler __instance) => TerminalItemDropRouter.AfterSingleHover(__instance);
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
internal static class TerminalGroupDropPatch
{
    private static void Prefix(ItemMultiSelectHandler __instance, out object? __state) =>
        __state = TerminalItemDropRouter.BeginGroupDrop(__instance);

    private static void Postfix(object? __state) => TerminalItemDropRouter.CompleteDrop(__state);

    private static Exception? Finalizer(object? __state, Exception? __exception)
    {
        if (__exception != null && __state is IDisposable groupScope) groupScope.Dispose();
        return __exception;
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "UpdateDropTarget")]
internal static class TerminalGroupHoverPatch
{
    private static void Postfix(ItemMultiSelectHandler __instance) => TerminalItemDropRouter.AfterGroupHover(__instance);
}
