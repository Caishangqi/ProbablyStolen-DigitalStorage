using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Opens the terminal's storage window instead of its native machinery window. Double-click, single-click interact
/// and the context-menu Open all call <c>OpenContentAction</c>.
/// </summary>
internal static class TerminalOpenPatch
{
    internal static GamePatchGroup Group() => new(GameFeature.TerminalOpen, GamePatchTier.Core,
        new GamePatch("ItemMouseDoubleClickHandler.OpenContentAction",
            () => AccessTools.Method(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenContentAction)),
            prefix: AccessTools.Method(typeof(TerminalOpenPatch), nameof(Prefix))));

    private static bool Prefix(GameItem newItem) => !TerminalOpenRouter.TryOpen(newItem);
}
