using System;
using System.Collections.Generic;
using System.Reflection;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

/// <summary>
/// The mod settings page. The native <c>ModConfigWindow</c> draws an int entry with a min and max as a slider and one
/// without as a text box, so numbers are defined without a range to let players type them,
/// and every read clamps the stored value. Each section becomes a header in the order entries are defined.
/// </summary>
internal sealed class DigitalStorageSettings : IGridPowerSettingsPort
{
    internal const int MaximumPowerUsage = 1000;
    // ConfigEntry.Section has an internal setter.
    private static readonly MethodInfo? SectionSetter =
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Section))?.GetSetMethod(nonPublic: true);
    private readonly ModText _text;
    private readonly List<KeyValuePair<ConfigEntry, string>> _sections = new();
    private readonly ConfigEntry<int> _controllerPowerUsage;
    private readonly ConfigEntry<int> _drivePowerUsage;
    private readonly ConfigEntry<int> _terminalPowerUsage;
    private readonly ConfigEntry<int> _engineerDailyChance;
    private readonly ConfigEntry<bool> _terminalDiagnostics;
    private readonly ConfigEntry<bool> _showCellPages;
    private readonly ConfigEntry<PinyinKeyboard> _pinyinKeyboard;
    private readonly ConfigEntry<PinyinSearchMode> _pinyinMode;
    private readonly ConfigEntry<bool> _zh2z;
    private readonly ConfigEntry<bool> _sh2s;
    private readonly ConfigEntry<bool> _ch2c;
    private readonly ConfigEntry<bool> _ang2an;
    private readonly ConfigEntry<bool> _ing2in;
    private readonly ConfigEntry<bool> _eng2en;
    private readonly ConfigEntry<bool> _u2v;
    private readonly ConfigEntry<SearchMode> _tagSearchMode;
    private readonly ConfigEntry<SearchMode> _typeSearchMode;
    private readonly ConfigEntry<SearchMode> _descriptionSearchMode;
    private readonly ConfigEntry<SearchMode> _identifierSearchMode;
    private readonly ConfigEntry<bool> _searchHighlighting;
    private bool _invalidPinyinModeLogged;

    internal DigitalStorageSettings(string modId, ModText text)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentException("A mod ID is required.", nameof(modId));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        _text = text;
        var config = ModHelper.GetConfig(modId);
        // The native panel shows the section string as the header, but Init runs before this mod's localization is
        // loaded, so these start as keys and RefreshSectionNames replaces them once the text is available.
        const string power = "config_section_power";
        const string debug = "config_section_debug";
        const string vendors = "config_section_vendors";
        const string search = "config_section_terminal_search";
        _controllerPowerUsage = config.Define("controller_power_usage", 3, power,
            () => text.Get("network_power_controller_name"), tooltip: () => text.Get("network_power_controller_desc"));
        _drivePowerUsage = config.Define("drive_power_usage", 2, power,
            () => text.Get("network_power_drive_name"), tooltip: () => text.Get("network_power_drive_desc"));
        _terminalPowerUsage = config.Define("terminal_power_usage", 1, power,
            () => text.Get("network_power_terminal_name"), tooltip: () => text.Get("network_power_terminal_desc"));
        _engineerDailyChance = config.Define("upper_engineer_daily_chance", 25, vendors,
            () => text.Get("upper_engineer_daily_chance_name"), tooltip: () => text.Get("upper_engineer_daily_chance_desc"));
        _showCellPages = config.Define("debug_show_cell_pages", false, debug,
            () => text.Get("debug_show_cell_pages_name"), tooltip: () => text.Get("debug_show_cell_pages_desc"));
        _terminalDiagnostics = config.Define("terminal_diagnostics", true, debug,
            () => text.Get("terminal_diagnostics_name"), tooltip: () => text.Get("terminal_diagnostics_desc"));
        _pinyinKeyboard = config.DefineEnum("terminal_search_keyboard", PinyinKeyboard.Quanpin, search,
            () => text.Get("terminal_search_keyboard_name"), () => text.Get("terminal_search_keyboard_desc"));
        _pinyinMode = config.DefineEnum(PinyinSearchModePolicy.Key, PinyinSearchMode.Auto, search,
            () => text.Get("terminal_search_pinyin_mode_name"), () => text.Get("terminal_search_pinyin_mode_desc"));
        _zh2z = DefineSearchBool(config, "terminal_search_zh2z", false, search, text, "zh2z");
        _sh2s = DefineSearchBool(config, "terminal_search_sh2s", false, search, text, "sh2s");
        _ch2c = DefineSearchBool(config, "terminal_search_ch2c", false, search, text, "ch2c");
        _ang2an = DefineSearchBool(config, "terminal_search_ang2an", false, search, text, "ang2an");
        _ing2in = DefineSearchBool(config, "terminal_search_ing2in", false, search, text, "ing2in");
        _eng2en = DefineSearchBool(config, "terminal_search_eng2en", false, search, text, "eng2en");
        _u2v = DefineSearchBool(config, "terminal_search_u2v", false, search, text, "u2v");
        _tagSearchMode = DefineSearchMode(config, "terminal_search_tags", SearchMode.RequirePrefix, search, text, "tags");
        _typeSearchMode = DefineSearchMode(config, "terminal_search_types", SearchMode.RequirePrefix, search, text, "types");
        _descriptionSearchMode = DefineSearchMode(config, "terminal_search_descriptions", SearchMode.Enabled, search, text, "descriptions");
        _identifierSearchMode = DefineSearchMode(config, "terminal_search_identifiers", SearchMode.Disabled, search, text, "identifiers");
        _searchHighlighting = DefineSearchBool(config, "terminal_search_highlighting", true, search, text, "highlighting");
        foreach (var (entry, section) in new (ConfigEntry, string)[]
        {
            (_controllerPowerUsage, power), (_drivePowerUsage, power), (_terminalPowerUsage, power),
            (_engineerDailyChance, vendors),
            (_showCellPages, debug), (_terminalDiagnostics, debug),
            (_pinyinKeyboard, search), (_pinyinMode, search), (_zh2z, search), (_sh2s, search), (_ch2c, search),
            (_ang2an, search), (_ing2in, search), (_eng2en, search), (_u2v, search),
            (_tagSearchMode, search), (_typeSearchMode, search), (_descriptionSearchMode, search),
            (_identifierSearchMode, search), (_searchHighlighting, search)
        })
        {
            _sections.Add(new KeyValuePair<ConfigEntry, string>(entry, section));
        }

    }

    /// <summary>
    /// Writes the localized section names into the entries, so the panel headers follow the current language.
    /// Call once this mod's localization is loaded and again after the settings are applied.
    /// </summary>
    internal void RefreshSectionNames()
    {
        if (SectionSetter == null) return;
        foreach (var pair in _sections)
        {
            SectionSetter.Invoke(pair.Key, new object[] { _text.Get(pair.Value) });
        }
    }

    internal SearchConfiguration GetSearchConfiguration(string? confirmedLocale, Action<string>? warning = null)
    {
        var rawMode = _pinyinMode.Value;
        var mode = PinyinSearchModePolicy.Normalize(rawMode);
        if (mode != rawMode && !_invalidPinyinModeLogged)
        {
            _invalidPinyinModeLogged = true;
            warning?.Invoke("Digital Storage: unknown pinyin search mode; using Auto.");
        }
        return new SearchConfiguration(
            _pinyinKeyboard.Value, PinyinSearchModePolicy.IsEnabled(mode, confirmedLocale),
            _zh2z.Value, _sh2s.Value, _ch2c.Value,
            _ang2an.Value, _ing2in.Value, _eng2en.Value, _u2v.Value, _tagSearchMode.Value,
            _typeSearchMode.Value, _descriptionSearchMode.Value, _identifierSearchMode.Value, _searchHighlighting.Value);
    }

    private static ConfigEntry<bool> DefineSearchBool(ModConfig config, string key, bool initial, string section,
        ModText text, string suffix) => config.Define(key, initial, section,
        () => text.Get("terminal_search_" + suffix + "_name"),
        tooltip: () => text.Get("terminal_search_" + suffix + "_desc"));

    private static ConfigEntry<SearchMode> DefineSearchMode(ModConfig config, string key, SearchMode initial,
        string section, ModText text, string suffix) => config.DefineEnum(key, initial, section,
        () => text.Get("terminal_search_" + suffix + "_name"),
        () => text.Get("terminal_search_" + suffix + "_desc"));

    internal bool GetTerminalDiagnostics() => _terminalDiagnostics.Value;

    /// <summary>Gets whether double-clicking a storage cell also shows its storage pages.</summary>
    internal bool GetShowCellPages() => _showCellPages.Value;

    /// <summary>Gets the daily chance, in percent, that the upper-level engineer visits.</summary>
    internal int GetEngineerDailyChance() => Clamp(_engineerDailyChance.Value, 0, 100);

    public int GetPowerUsage(GridNodeKind kind) => Clamp(kind switch
    {
        GridNodeKind.Controller => _controllerPowerUsage.Value,
        GridNodeKind.Drive => _drivePowerUsage.Value,
        GridNodeKind.Terminal => _terminalPowerUsage.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    }, 0, MaximumPowerUsage);

    private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(maximum, value));
}
