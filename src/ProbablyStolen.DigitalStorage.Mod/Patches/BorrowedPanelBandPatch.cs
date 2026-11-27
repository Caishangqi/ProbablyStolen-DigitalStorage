using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Lets native box selection start over native panels shown inside a draggable mod window, and select there under
/// the running build's native rule (<see cref="BorrowedPanelInput.Scope"/>). The native checks run unchanged; the
/// patches only correct their answer over a borrowed panel, through <see cref="BorrowedPanelInput"/>. Both band
/// patches are applied, and the one that does not match the running build's rule returns at once.
/// </summary>
internal static class BorrowedPanelBandPatch
{
    internal static GamePatchGroup Group() => new GamePatchGroup(GameFeature.BorrowedPanelBand, GamePatchTier.Enhancement,
            new GamePatch("ItemMultiSelectHandler.IsPointerOverDragSurface",
                () => AccessTools.Method(typeof(ItemMultiSelectHandler), "IsPointerOverDragSurface"),
                postfix: AccessTools.Method(typeof(BorrowedPanelBandPatch), nameof(SurfacePostfix))),
            new GamePatch("ItemMultiSelectHandler.ComputeBandItems",
                () => AccessTools.Method(typeof(ItemMultiSelectHandler), "ComputeBandItems"),
                prefix: AccessTools.Method(typeof(BorrowedPanelBandPatch), nameof(GridPrefix)),
                postfix: AccessTools.Method(typeof(BorrowedPanelBandPatch), nameof(WindowPostfix))))
        .Requires("ItemMultiSelectHandler.bandWindow or bandInventory/bandLocksOnTouch",
            () => BorrowedPanelInput.Scope != BandScope.None);

    private static void SurfacePostfix(Vector2 screenPosition, ref bool __result) =>
        __result = BorrowedPanelInput.IsDragSurface(screenPosition, __result);

    private static void WindowPostfix(ItemMultiSelectHandler __instance, Rect bandRect, List<GameItemElement> __result) =>
        BorrowedPanelInput.AddBorrowedItems(__instance, bandRect, __result);

    private static void GridPrefix(ItemMultiSelectHandler __instance, Rect bandRect) =>
        BorrowedPanelInput.LockBorrowedGrid(__instance, bandRect);
}
