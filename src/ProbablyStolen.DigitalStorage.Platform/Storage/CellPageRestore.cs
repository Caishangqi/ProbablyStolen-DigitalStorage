using System;
using System.Linq;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Rebuilds a cell's pages while native decoding restores a save, and reports whether the decode hook is active.
/// <c>SaveManager.DecodeNodesInternal</c> creates each item from its factory (the anchor page), restores its
/// <c>itemState</c> (including the schema and layout tags), calls <c>StorageExpansionHelper.RestoreTabs</c>, and only
/// then reattaches children by inventory index; the vanilla storage bay expansion relies on the same call
/// (SaveManager.decompiled.cs:160-243). Pages are rebuilt from the recorded layout, or from
/// the schema 1 page count, whatever the cell's migration state, so locked cells keep their items when saved.
/// The Harmony postfix that calls <see cref="Restore"/> lives in the IMod assembly, the only assembly the game's
/// loader patches.
/// </summary>
public static class CellPageRestore
{
    private static string? _harmonyId;

    private static Exception? _lastFailure;

    /// <summary>
    /// Gets and clears the last failure raised while restoring, so each load reports only its own. A load that never
    /// reaches the load audit leaves its failure to be reported by the next audit; the affected cells are re-checked
    /// there anyway.
    /// </summary>
    internal static Exception? TakeLastFailure()
    {
        var failure = _lastFailure;
        _lastFailure = null;
        return failure;
    }

    /// <summary>Records the Harmony ID the game's loader uses for this mod (<c>"mod." + manifest ID</c>).</summary>
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
    }

    /// <summary>Gets whether this mod's loader-owned Harmony instance patched the decode hook; storing is disabled otherwise.</summary>
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
