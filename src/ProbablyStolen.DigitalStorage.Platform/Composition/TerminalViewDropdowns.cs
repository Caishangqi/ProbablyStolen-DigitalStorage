using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

/// <summary>
/// Shows localized option names in the terminal view dropdowns of this mod's native settings window. The native
/// panel lists enum names (DefineEnum stores <c>Enum.GetNames</c> as the choices); only the shown labels change, so
/// the stored values stay the enum names.
/// </summary>
public static class TerminalViewDropdowns
{
    private const string ModId = "digital_storage";
    private static readonly (string Key, Dictionary<string, string> Labels)[] Dropdowns =
    {
        (DigitalStorageSettings.SortOrderKey, new Dictionary<string, string>
        {
            ["Name"] = "terminal_sort_name", ["Amount"] = "terminal_sort_amount"
        }),
        (DigitalStorageSettings.SortDirectionKey, new Dictionary<string, string>
        {
            ["Ascending"] = "terminal_direction_asc", ["Descending"] = "terminal_direction_desc"
        }),
        (DigitalStorageSettings.SizeKey, SizeLabels()),
        (DigitalStorageSettings.PartitionSizeKey, SizeLabels())
    };
    private static bool _failureLogged;

    private static Dictionary<string, string> SizeLabels() => new()
    {
        ["Small"] = "terminal_size_small", ["Medium"] = "terminal_size_medium",
        ["Tall"] = "terminal_size_tall", ["Full"] = "terminal_size_full"
    };

    /// <summary>Changes displayed labels while preserving stored enum values and the current selection.</summary>
    public static void Localize(LoadedMod? mod, CustomUIWindow? window)
    {
        if (mod?.Manifest == null || window == null || !window.IsAlive ||
            !string.Equals(mod.Manifest.ID, ModId, StringComparison.Ordinal)) return;
        var config = ModConfig.Find(ModId);
        foreach (var (key, labels) in Dropdowns)
        {
            var choices = config?.Get(key)?.Choices;
            var element = window.Get(key);
            if (choices == null || element == null || !element.IsAlive || element.dropdown == null ||
                !choices.All(labels.ContainsKey)) continue;
            try
            {
                var index = element.GetIndex();
                element.SetOptions(choices.Select(choice => Translate(labels[choice], choice)).ToArray());
                element.SetIndex(index, notify: false);
                element.dropdown.RefreshShownValue();
            }
            catch (Exception error)
            {
                LogFailure(error);
            }
        }
    }

    private static string Translate(string key, string fallback)
    {
        var value = ModHelper.GetLocalized(ModId, key);
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static void LogFailure(Exception error)
    {
        if (_failureLogged) return;
        _failureLogged = true;
        UnityEngine.Debug.LogError($"Digital Storage: could not localize terminal view dropdowns ({error.GetType().Name}).");
    }
}
