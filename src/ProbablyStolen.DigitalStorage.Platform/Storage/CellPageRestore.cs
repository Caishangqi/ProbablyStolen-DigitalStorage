using System;
using System.Linq;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Rebuilds a cell's pages while native decoding restores a save, and reports whether the decode hook is active.
/// <c>SaveManager.DecodeNodesInternal</c> creates each item from its factory (the anchor page), restores its
/// <c>itemState</c> (including the schema and layout tags), calls <c>StorageExpansionHelper.RestoreTabs</c>, and only
/// then reattaches children by inventory index; the vanilla storage bay expansion relies on the same call.
/// Pages are rebuilt from the recorded layout, or from
/// the schema 1 page count, whatever the cell's migration state, so locked cells keep their items when saved.
/// The Harmony postfix that calls <see cref="Restore"/> is applied under this mod's Harmony id
/// (<c>"mod." + manifest ID</c>), which the loader removes when it stops the mod.
/// </summary>
public static class CellPageRestore
{
    private static string? _harmonyId;

    private static Exception? _lastFailure;

    /// <summary>
    /// Gets and clears the last failure raised while restoring, so each load reports only its own. A load that never
    /// reaches the load audit leaves its failure to be reported by the next audit; the affected cells are checked again
    /// there anyway.
    /// </summary>
    internal static Exception? TakeLastFailure()
    {
        var failure = _lastFailure;
        _lastFailure = null;
        return failure;
    }

    /// <summary>Records the Harmony ID this mod's patches are applied under (<c>"mod." + manifest ID</c>).</summary>
    internal static void SetModId(string modId) => _harmonyId = "mod." + modId;

    /// <summary>Rebuilds the recorded pages of one decoded item; other items are ignored.</summary>
    public static void Restore(GameItem item)
    {
        try
        {
            if (item != null && CellPages.Tabs(item) != null) CellPages.RestorePages(item);
        }
        catch (Exception error)
        {
            // An exception here would abort decoding of the whole save; the load audit reports the cell instead.
            _lastFailure = error;
        }

        try
        {
            // Tags are restored before this postfix and children after it, so the expanded bay shape must be in place
            // before saved modules are put back into its extra cells, even when the pages failed to restore.
            if (item != null) ModuleBayExpansionHooks.Initialize(item);
        }
        catch (Exception error)
        {
            _lastFailure ??= error;
        }
    }

    /// <summary>Storing requires the decode hook to be patched under this mod's Harmony ID.</summary>
    internal static bool IsApplied()
    {
        if (_harmonyId == null)
        {
            return false;
        }

        try
        {
            return Harmony.GetAllPatchedMethods()
                .Where(method => method.DeclaringType == typeof(StorageExpansionHelper)
                    && method.Name == nameof(StorageExpansionHelper.RestoreTabs))
                .Any(method => Harmony.GetPatchInfo(method)?.Postfixes.Any(patch => patch.owner == _harmonyId) == true);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
