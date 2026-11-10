using System;
using System.Linq;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Rebuilds appended cell pages while native decoding restores a save, and reports whether the decode hook is
/// active. <c>SaveManager.DecodeNodesInternal</c> creates each item from its factory (one page), restores its
/// <c>itemState</c> (including the page count tag), calls <c>StorageExpansionHelper.RestoreTabs</c>, and only then
/// reattaches children by inventory index; the vanilla storage bay expansion relies on the same call
/// (Steam build 26496118, SaveManager.decompiled.cs:160-243). The Harmony postfix that calls <see cref="Restore"/>
/// lives in the IMod assembly, the only assembly the game's loader patches.
/// </summary>
public static class CellPageRestore
{
    private static string? _harmonyId;

    /// <summary>Gets the last failure raised while restoring, for the load audit to report.</summary>
    internal static Exception? LastFailure { get; private set; }

    /// <summary>Records the Harmony ID the game's loader uses for this mod (<c>"mod." + manifest ID</c>).</summary>
    internal static void SetModId(string modId) => _harmonyId = "mod." + modId;

    /// <summary>Restores the recorded page count of one decoded item; other items are ignored.</summary>
    public static void Restore(GameItem item)
    {
        try
        {
            if (item != null && CellPages.Tabs(item) != null)
            {
                if (!CellTags.HasValidPageCount(item))
                    throw new InvalidOperationException("The recorded cell page count is invalid.");
                CellPages.EnsurePageCount(item, CellTags.ReadPageCount(item));
            }
        }
        catch (Exception error)
        {
            // An exception here would abort decoding of the whole save; the load audit reports the cell instead.
            LastFailure = error;
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
