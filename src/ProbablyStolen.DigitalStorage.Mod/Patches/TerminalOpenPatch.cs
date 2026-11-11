using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Opens the terminal's storage window instead of its native machinery window. Double-click, single-click interact
/// and the context-menu Open all call <c>OpenContentAction</c>. Like
/// <see cref="CellPageRestorePatch"/>, it lives in the IMod assembly that the loader patches.
/// </summary>
[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenContentAction))]
internal static class TerminalOpenPatch
{
    private static bool Prefix(GameItem newItem) => !TerminalOpenRouter.TryOpen(newItem);
}
