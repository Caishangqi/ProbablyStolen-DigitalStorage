using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Lets native box selection start over native panels shown inside a draggable mod window. The native check runs
/// unchanged; the postfix only corrects its answer over a borrowed panel, through <see cref="BorrowedPanelInput"/>.
/// </summary>
[HarmonyPatch(typeof(ItemMultiSelectHandler), "IsPointerOverDragSurface")]
internal static class BorrowedPanelBandPatch
{
    private static void Postfix(Vector2 screenPosition, ref bool __result) =>
        __result = BorrowedPanelInput.IsDragSurface(screenPosition, __result);
}

/// <summary>Rebuilds the selection within a borrowed panel's window when the native graph cannot identify it.</summary>
[HarmonyPatch(typeof(ItemMultiSelectHandler), "ComputeBandItems")]
internal static class BorrowedPanelBandItemsPatch
{
    private static void Postfix(PixelWindow? ___bandWindow, Rect bandRect, List<GameItemElement> __result) =>
        BorrowedPanelInput.AddBorrowedItems(___bandWindow, bandRect, __result);
}

/// <summary>Prefers the first eligible bay among ties in registered machine grids.</summary>
[HarmonyPatch(typeof(GridPixelElement), nameof(GridPixelElement.TryFindOneValidInventorySlot))]
internal static class ForwardFillGridPatch
{
    private static bool Prefix(GridPixelElement __instance, GameItem item, ref SlotMarker? __result) =>
        !ForwardFillGrids.TryFind(__instance, item, out __result);
}
