using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Keeps the store's back key from closing the topmost native window while the partition editor has it. The store
/// resolves the key on its own (StoreUIManager.Update calls <c>ResolveEscape</c> unless one of four native systems
/// consumed it) without asking custom windows, so the workbench under the editor would
/// close and take the editor and its draft with it. The editor handles the key itself: menu, then detail page, then
/// the editor.
/// </summary>
[HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.ResolveEscape))]
internal static class StoreUIManagerEscapePatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!PartitionEditorInput.OwnsEscape()) return true;
        __result = true;
        return false;
    }
}
