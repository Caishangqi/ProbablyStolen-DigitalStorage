using System;
using System.Collections.Generic;
using System.Reflection;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

/// <summary>
/// The mod settings page. The native <c>ModConfigWindow</c> draws an int entry with a min and max as a slider and one
/// without as a text box (Steam build 26496118), so numbers are defined without a range to let players type them,
/// and every read clamps the stored value. Each section becomes a header in the order entries are defined.
/// </summary>
internal sealed class DigitalStorageSettings : IGridPowerSettingsPort
{
    internal const int MaximumPowerUsage = 1000;
    // ConfigEntry.Section has an internal setter (Steam build 26525552).
    private static readonly MethodInfo? SectionSetter =
        typeof(ConfigEntry).GetProperty(nameof(ConfigEntry.Section))?.GetSetMethod(nonPublic: true);
    private readonly ModText _text;
    private readonly List<KeyValuePair<ConfigEntry, string>> _sections = new();
    private readonly ConfigEntry<int> _controllerPowerUsage;
    private readonly ConfigEntry<int> _drivePowerUsage;
    private readonly ConfigEntry<int> _terminalPowerUsage;
    private readonly ConfigEntry<int> _cellMaxPages;
    private readonly ConfigEntry<int> _engineerDailyChance;
    private readonly ConfigEntry<bool> _terminalDiagnostics;
    private readonly ConfigEntry<bool> _showCellPages;

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
        const string storage = "config_section_storage";
        const string debug = "config_section_debug";
        const string vendors = "config_section_vendors";
        _controllerPowerUsage = config.Define("controller_power_usage", 3, power,
            () => text.Get("network_power_controller_name"), tooltip: () => text.Get("network_power_controller_desc"));
        _drivePowerUsage = config.Define("drive_power_usage", 2, power,
            () => text.Get("network_power_drive_name"), tooltip: () => text.Get("network_power_drive_desc"));
        _terminalPowerUsage = config.Define("terminal_power_usage", 1, power,
            () => text.Get("network_power_terminal_name"), tooltip: () => text.Get("network_power_terminal_desc"));
        _cellMaxPages = config.Define("storage_cell_max_pages", CellLayout.Cell1K.MaxPages, storage,
            () => text.Get("storage_cell_max_pages_name"), tooltip: () => text.Get("storage_cell_max_pages_desc"));
        _engineerDailyChance = config.Define("upper_engineer_daily_chance", 25, vendors,
            () => text.Get("upper_engineer_daily_chance_name"), tooltip: () => text.Get("upper_engineer_daily_chance_desc"));
        _showCellPages = config.Define("debug_show_cell_pages", false, debug,
            () => text.Get("debug_show_cell_pages_name"), tooltip: () => text.Get("debug_show_cell_pages_desc"));
        _terminalDiagnostics = config.Define("terminal_diagnostics", true, debug,
            () => text.Get("terminal_diagnostics_name"), tooltip: () => text.Get("terminal_diagnostics_desc"));
        foreach (var (entry, section) in new (ConfigEntry, string)[]
        {
            (_controllerPowerUsage, power), (_drivePowerUsage, power), (_terminalPowerUsage, power),
            (_cellMaxPages, storage), (_engineerDailyChance, vendors),
            (_showCellPages, debug), (_terminalDiagnostics, debug)
        })
        {
            _sections.Add(new KeyValuePair<ConfigEntry, string>(entry, section));
        }

        RefreshSectionNames();
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

    internal bool GetTerminalDiagnostics() => _terminalDiagnostics.Value;

    /// <summary>Gets whether double-clicking a storage cell also shows its storage pages.</summary>
    internal bool GetShowCellPages() => _showCellPages.Value;

    /// <summary>Gets the daily chance, in percent, that the upper-level engineer visits.</summary>
    internal int GetEngineerDailyChance() => Clamp(_engineerDailyChance.Value, 0, 100);

    /// <summary>Applies the configured 1k base page limit.</summary>
    internal void ApplyStorageLimits() => CellPages.Configure(Clamp(_cellMaxPages.Value, 1, CellLayout.MaxBasePages));

    public int GetPowerUsage(GridNodeKind kind) => Clamp(kind switch
    {
        GridNodeKind.Controller => _controllerPowerUsage.Value,
        GridNodeKind.Drive => _drivePowerUsage.Value,
        GridNodeKind.Terminal => _terminalPowerUsage.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    }, 0, MaximumPowerUsage);

    private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(maximum, value));
}
