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
