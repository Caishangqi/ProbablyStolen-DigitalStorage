using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Closes the partition editor with its workbench window or when its cell is picked up. The workbench offers the
/// editor only while this group is active, so an editor never outlives its cell.
/// </summary>
internal static class PartitionEditorClosePatches
{
    internal static GamePatchGroup Group() => new(GameFeature.PartitionEditorClose, GamePatchTier.Core,
        new GamePatch("PixelWindow.Hide", () => AccessTools.Method(typeof(PixelWindow), nameof(PixelWindow.Hide)),
            postfix: AccessTools.Method(typeof(PartitionEditorClosePatches), nameof(WindowHiddenPostfix))),
        new GamePatch("ItemMouseDragHandler.StartDrag", () => AccessTools.Method(typeof(ItemMouseDragHandler), "StartDrag"),
            postfix: AccessTools.Method(typeof(PartitionEditorClosePatches), nameof(DragStartedPostfix))));

    /// <summary>
    /// Every way a window hides runs <c>PixelWindow.Hide</c>: the close button, WindowsHandler (back key, toggling), a
    /// drag of the item (<c>ItemMouseDragHandler.StartDrag</c>), <c>Lock</c> and <c>Destroy</c>.
    /// </summary>
    private static void WindowHiddenPostfix(PixelWindow __instance) => PartitionEditorInput.WindowHidden(__instance);

    /// <summary>
    /// The item stays in its slot until it is dropped, so no inventory event marks the pickup; the drag start does.
    /// </summary>
    private static void DragStartedPostfix(ItemMouseDragHandler __instance) => PartitionEditorInput.DragStarted(__instance);
}
