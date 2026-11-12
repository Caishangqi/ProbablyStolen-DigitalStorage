using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Reports hidden native windows to the partition editor, which closes with its workbench window. Every way a window
/// hides runs <c>PixelWindow.Hide</c>: the close button, WindowsHandler (back key, toggling), a drag of the item
/// (ItemMouseDragHandler.StartDrag), <c>Lock</c> and <c>Destroy</c>.
/// </summary>
[HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Hide))]
internal static class PixelWindowHidePatch
{
    private static void Postfix(PixelWindow __instance) => PartitionEditorInput.WindowHidden(__instance);
}

/// <summary>
/// Reports a started native drag to the partition editor, which closes when its cell is picked up. The item stays in
/// its slot until it is dropped, so no inventory event marks the pickup. The method is private and named literally.
/// </summary>
[HarmonyPatch(typeof(ItemMouseDragHandler), "StartDrag")]
internal static class ItemDragStartPatch
{
    private static void Postfix(ItemMouseDragHandler __instance) => PartitionEditorInput.DragStarted(__instance);
}
