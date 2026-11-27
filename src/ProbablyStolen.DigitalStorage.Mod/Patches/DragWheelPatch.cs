using HarmonyLib;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Skips the native drag's wheel handling in a frame a sample window claimed the wheel, so scrolling that window
/// neither rotates the held item nor changes its quantity (<see cref="DragWheel"/>).
/// </summary>
internal static class DragWheelPatch
{
    internal static GamePatchGroup Group() => new(GameFeature.TerminalSampleWheel, GamePatchTier.Enhancement,
        new GamePatch("ItemMouseDragHandler.OnEventMouseScroll",
            () => AccessTools.Method(typeof(ItemMouseDragHandler), "OnEventMouseScroll", new[] { typeof(Vector2) }),
            prefix: AccessTools.Method(typeof(DragWheelPatch), nameof(Prefix))));

    private static bool Prefix() => !DragWheel.IsClaimed;
}
