using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProbablyStolen.DigitalStorage.Api.Integration;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Integration;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

/// <summary>
/// The mod settings page. The native <c>ModConfigWindow</c> draws an int entry with a min and max as a slider and one
/// without as a text box, so numbers are defined without a range to let players type them,
/// and every read clamps the stored value. Each section becomes a header in the order entries are defined.
/// </summary>
internal sealed class DigitalStorageSettings : IGridPowerSettingsPort, ITerminalViewSettings, IIntegrationSettings
{
    internal const int MaximumPowerUsage = 1000;
    internal const string SortOrderKey = "terminal_view_sort";
    internal const string SortDirectionKey = "terminal_view_direction";
    internal const string SizeKey = "terminal_view_size";
    internal const string PartitionSizeKey = "partition_view_size";
    // ConfigEntry.Section has an internal setter.
    private static readonly MethodInfo? SectionSetter =
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Section))?.GetSetMethod(nonPublic: true);
    private readonly ModText _text;
    private readonly ModConfig _config;
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
    private readonly ConfigEntry<TerminalSortOrder> _sortOrder;
    private readonly ConfigEntry<TerminalSortDirection> _sortDirection;
    private readonly ConfigEntry<TerminalSize> _size;
    private readonly ConfigEntry<TerminalSize> _partitionSize;
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
        _config = config;
        // The native panel shows the section string as the header, but Init runs before this mod's localization is
        // loaded, so these start as keys and RefreshSectionNames replaces them once the text is available.
        const string power = "config_section_power";
        const string debug = "config_section_debug";
        const string vendors = "config_section_vendors";
        const string search = "config_section_terminal_search";
        const string view = "config_section_terminal_view";
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
        _sortOrder = config.DefineEnum(SortOrderKey, TerminalSortOrder.Name, view,
            () => text.Get("terminal_view_sort_name"), () => text.Get("terminal_view_sort_desc"));
        _sortDirection = config.DefineEnum(SortDirectionKey, TerminalSortDirection.Ascending, view,
            () => text.Get("terminal_view_direction_name"), () => text.Get("terminal_view_direction_desc"));
        _size = config.DefineEnum(SizeKey, TerminalSize.Small, view,
            () => text.Get("terminal_view_size_name"), () => text.Get("terminal_view_size_desc"));
        _partitionSize = config.DefineEnum(PartitionSizeKey, TerminalSize.Medium, view,
            () => text.Get("partition_view_size_name"), () => text.Get("partition_view_size_desc"));
        foreach (var (entry, section) in new (ConfigEntry, string)[]
        {
            (_controllerPowerUsage, power), (_drivePowerUsage, power), (_terminalPowerUsage, power),
            (_engineerDailyChance, vendors),
            (_showCellPages, debug), (_terminalDiagnostics, debug),
            (_pinyinKeyboard, search), (_pinyinMode, search), (_zh2z, search), (_sh2s, search), (_ch2c, search),
            (_ang2an, search), (_ing2in, search), (_eng2en, search), (_u2v, search),
            (_tagSearchMode, search), (_typeSearchMode, search), (_descriptionSearchMode, search),
            (_identifierSearchMode, search), (_searchHighlighting, search),
            (_sortOrder, view), (_sortDirection, view), (_size, view), (_partitionSize, view)
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

    /// <summary>
    /// Defines an integration switch in the integrations section, after every other section. The integration host
    /// calls this one frame after Init, once the other mod's version is known; <c>ModConfig</c> applies the saved value
    /// whenever an entry is defined. Verified versions of
    /// the other mod share one key, so a player's choice survives its updates; an unverified version gets a key of its
    /// own that starts off, so an update of the other mod never runs untested code because of an earlier choice.
    /// </summary>
    public (Func<bool> Switch, IIntegrationConfig Config) Define(IIntegrationPlugin plugin, string modVersion, bool verified,
        Func<string?> status)
    {
        if (plugin == null) throw new ArgumentNullException(nameof(plugin));
        if (status == null) throw new ArgumentNullException(nameof(status));
        // Plugin properties are read here, inside the host's guarded call, so the native settings panel never runs
        // plugin code. The native panel orders headers by their first entry, and integrations are defined after every
        // built-in entry (once all mods are loaded), so each plugin's header follows the built-in ones.
        var section = plugin.SettingSectionKey;
        if (!PluginKey.IsMatch(section ?? string.Empty) || _sections.Any(pair => pair.Value == section))
            throw new InvalidOperationException($"The settings header {section} is not unique.");
        var labelKey = plugin.SettingLabelKey;
        var tooltipKey = plugin.SettingTooltipKey;
        var entry = _config.Define(IntegrationKey(plugin.PluginUid, verified ? null : modVersion), verified, section,
            () => _text.Get(labelKey), tooltip: () => IntegrationTooltip(tooltipKey, status));
        AddIntegrationEntry(entry, section!);
        return (() => entry.Value, new PluginConfig(this, plugin.PluginUid, section!));
    }

    private static readonly System.Text.RegularExpressions.Regex PluginKey = new("^[a-z0-9_]+$");

    private void AddIntegrationEntry(ConfigEntry entry, string section)
    {
        _sections.Add(new KeyValuePair<ConfigEntry, string>(entry, section));
        // Defined after Init, possibly after the section names were localized; name this one now as well.
        SectionSetter?.Invoke(entry, new object[] { _text.Get(section) });
    }

    /// <summary>One plugin's options: keys are stored under the plugin id, entries go below its switch.</summary>
    private sealed class PluginConfig : IIntegrationConfig
    {
        private readonly DigitalStorageSettings _owner;
        private readonly string _prefix;
        private readonly string _section;
        private readonly HashSet<string> _keys = new();

        internal PluginConfig(DigitalStorageSettings owner, string pluginUid, string section)
        {
            _owner = owner;
            _prefix = IntegrationPrefix(pluginUid);
            _section = section;
        }

        public IIntegrationOption<bool> DefineBool(string key, bool defaultValue, string labelKey, string tooltipKey)
        {
            var entry = _owner._config.Define(Key(key), defaultValue, _section, () => _owner._text.Get(labelKey),
                tooltip: () => _owner._text.Get(tooltipKey));
            _owner.AddIntegrationEntry(entry, _section);
            return new Option<bool>(() => entry.Value);
        }

        public IIntegrationOption<int> DefineInt(string key, int defaultValue, int min, int max, string labelKey,
            string tooltipKey)
        {
            if (min > max || defaultValue < min || defaultValue > max)
                throw new ArgumentOutOfRangeException(nameof(defaultValue), defaultValue, "The default must lie in the range.");
            // No native range: the panel then shows a text box players can type into; reads clamp, as built-in numbers.
            var entry = _owner._config.Define(Key(key), defaultValue, _section, () => _owner._text.Get(labelKey),
                tooltip: () => _owner._text.Get(tooltipKey));
            _owner.AddIntegrationEntry(entry, _section);
            return new Option<int>(() => Math.Min(max, Math.Max(min, entry.Value)));
        }

        private string Key(string key)
        {
            if (!PluginKey.IsMatch(key ?? string.Empty) || key == "enabled")
                throw new ArgumentException($"Integration option keys use lowercase letters, digits and '_': {key}", nameof(key));
            if (!_keys.Add(key!)) throw new ArgumentException($"The integration option {key} is defined twice.", nameof(key));
            return _prefix + "option_" + key;
        }
    }

    private sealed class Option<T> : IIntegrationOption<T>
    {
        private readonly Func<T> _read;

        internal Option(Func<T> read) => _read = read;

        public T Value => _read();
    }

    /// <summary>Gets the settings key of an integration switch; <paramref name="unverifiedVersion"/> is null for verified versions.</summary>
    internal static string IntegrationKey(string pluginUid, string? unverifiedVersion)
    {
        var prefix = IntegrationPrefix(pluginUid);
        return unverifiedVersion == null
            ? $"{prefix}enabled"
            : $"{prefix}enabled_untested_{EscapeVersion(unverifiedVersion)}";
    }

    // Plugin ids are this mod's own, so a readable form is enough; versions come from other mods and are escaped.
    private static string IntegrationPrefix(string pluginUid) =>
        "integration_" + new string(pluginUid.Select(c => c < 128 && char.IsLetterOrDigit(c) ? c : '_').ToArray()) + "_";

    // Injective: ASCII letters, digits, '.' and '-' stay; any other character, '_' included, becomes '_' and its code,
    // so an unknown version ("_") never meets a real one.
    private static string EscapeVersion(string version) => version.Length == 0
        ? "_"
        : string.Concat(version.Select(c =>
            c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == '-') ? c.ToString() : "_" + ((int)c).ToString("x4")));

    private string IntegrationTooltip(string tooltipKey, Func<string?> status)
    {
        var description = _text.Get(tooltipKey);
        string? state;
        try
        {
            state = status();
        }
        catch (Exception)
        {
            state = null;
        }

        return state == null ? description : description + "\n\n" + _text.Get(state);
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

    public TerminalSortOrder SortOrder
    {
        get => TerminalViewPolicy.Normalize(_sortOrder.Value);
        set => _sortOrder.Set(value);
    }

    public TerminalSortDirection SortDirection
    {
        get => TerminalViewPolicy.Normalize(_sortDirection.Value);
        set => _sortDirection.Set(value);
    }

    public TerminalSize Size
    {
        get => TerminalViewPolicy.Normalize(_size.Value);
        set => _size.Set(value);
    }

    public TerminalSize PartitionSize
    {
        get => TerminalViewPolicy.Normalize(_partitionSize.Value, TerminalSize.Medium);
        set => _partitionSize.Set(value);
    }

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
