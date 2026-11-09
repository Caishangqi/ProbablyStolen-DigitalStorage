using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Ports;

using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Energy;

internal sealed class NativePowerConfiguration : IGridPowerSettingsPort
{
    private const int MaximumPowerUsage = 1000;
    private const int MaximumCellTypes = 63;
    private const int MinimumCellCapacityBytes = 512;
    private const int MaximumCellCapacityBytes = 8192;
    private readonly ConfigEntry<int> _controllerPowerUsage;
    private readonly ConfigEntry<int> _drivePowerUsage;
    private readonly ConfigEntry<int> _terminalPowerUsage;
    private readonly ConfigEntry<bool> _terminalDiagnostics;

    internal NativePowerConfiguration(string modId, ModText text)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentException("A mod ID is required.", nameof(modId));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var config = ModHelper.GetConfig(modId);
        _controllerPowerUsage = config.Define("controller_power_usage", 3, "Network Power",
            () => text.Get("network_power_controller_name"), min: 0, max: MaximumPowerUsage,
            tooltip: () => text.Get("network_power_controller_desc"));
        _drivePowerUsage = config.Define("drive_power_usage", 2, "Network Power",
            () => text.Get("network_power_drive_name"), min: 0, max: MaximumPowerUsage,
            tooltip: () => text.Get("network_power_drive_desc"));
        _terminalPowerUsage = config.Define("terminal_power_usage", 1, "Network Power",
            () => text.Get("network_power_terminal_name"), min: 0, max: MaximumPowerUsage,
            tooltip: () => text.Get("network_power_terminal_desc"));
        // These three entries keep their approved 1k values, localization keys, and any stored user values.
        // No component reads them until an approved cell backend exists.
        config.Define("storage_cell_capacity_bytes", 1024, "Storage",
            () => text.Get("storage_cell_capacity_name"), min: MinimumCellCapacityBytes,
            max: MaximumCellCapacityBytes, tooltip: () => text.Get("storage_cell_capacity_desc"));
        config.Define("storage_cell_max_types", MaximumCellTypes, "Storage",
            () => text.Get("storage_cell_max_types_name"), min: 1, max: MaximumCellTypes,
            tooltip: () => text.Get("storage_cell_max_types_desc"));
        config.Define("extraction_power_per_item", 1, "Extraction",
            () => text.Get("extraction_power_per_item_name"),
            tooltip: () => text.Get("extraction_power_per_item_desc"));
        _terminalDiagnostics = config.Define("terminal_diagnostics", true, "Diagnostics",
            () => text.Get("terminal_diagnostics_name"),
            tooltip: () => text.Get("terminal_diagnostics_desc"));
    }

    internal bool GetTerminalDiagnostics() => _terminalDiagnostics.Value;

    public int GetPowerUsage(GridNodeKind kind) => kind switch
    {
        GridNodeKind.Controller => _controllerPowerUsage.Value,
        GridNodeKind.Drive => _drivePowerUsage.Value,
        GridNodeKind.Terminal => _terminalPowerUsage.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
