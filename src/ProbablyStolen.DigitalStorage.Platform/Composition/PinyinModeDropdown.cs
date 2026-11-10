using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

/// <summary>Changes only the displayed labels of this mod's native pinyin mode dropdown.</summary>
public static class PinyinModeDropdown
{
    private const string ModId = "digital_storage";

    /// <summary>Localizes the mode dropdown in this mod's live settings window without changing its choices.</summary>
    public static void Localize(LoadedMod? mod, CustomUIWindow? window)
    {
        if (mod == null || window == null || !window.IsAlive || mod.Manifest == null ||
            !string.Equals(mod.Manifest.ID, ModId, StringComparison.Ordinal)) return;

        var config = ModConfig.Find(ModId);
        var entry = config?.Get(PinyinSearchModePolicy.Key);
        var element = window.Get(PinyinSearchModePolicy.Key);
        if (entry == null || entry.ValueType != typeof(PinyinSearchMode) ||
            !PinyinSearchModePolicy.HasCanonicalChoices(entry.Choices) || element == null || !element.IsAlive ||
            element.dropdown == null) return;

        var index = element.GetIndex();
        var dropdown = element.dropdown;
        var labels = new List<string>(3);
        for (var i = 0; i < PinyinSearchModePolicy.Choices.Length; i++)
        {
            var key = "terminal_search_pinyin_mode_option_" + PinyinSearchModePolicy.Choices[i].ToLowerInvariant();
            var translated = PinyinSearchModePolicy.Choices[i];
            try
            {
                var value = ModHelper.GetLocalized(ModId, key);
                if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, key, StringComparison.Ordinal))
                    translated = value;
            }
            catch (Exception error)
            {
                LogFailure(error);
            }
            labels.Add(translated);
        }

        PinyinDropdownDisplay.Apply(mod.Manifest.ID, window.IsAlive, PinyinSearchModePolicy.Key,
            entry.ValueType, entry.Choices, index, labels.ToArray(), element.IsAlive, dropdown != null,
            options => element.SetOptions(options), selectedIndex => element.SetIndex(selectedIndex, notify: false),
            () => dropdown!.RefreshShownValue(), LogFailure);
    }

    private static void LogFailure(Exception error)
    {
        UnityEngine.Debug.LogError($"Digital Storage: could not localize pinyin mode dropdown ({error.GetType().Name}).");
    }
}
