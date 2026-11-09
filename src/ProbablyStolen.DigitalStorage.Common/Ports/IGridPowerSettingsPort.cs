using ProbablyStolen.DigitalStorage.Common.Domain.Network;

namespace ProbablyStolen.DigitalStorage.Common.Ports;

/// <summary>Supplies the current game-configured integer power draw for each machine role.</summary>
public interface IGridPowerSettingsPort
{
    /// <summary>Returns one non-negative game-native power value for a machine role.</summary>
    int GetPowerUsage(GridNodeKind kind);
}
