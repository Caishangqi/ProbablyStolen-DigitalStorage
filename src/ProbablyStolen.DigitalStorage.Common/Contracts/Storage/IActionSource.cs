using System;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

/// <summary>Who started a storage operation; used for logs and later permissions.</summary>
public interface IActionSource
{
    /// <summary>Gets the player name, or null when no player started the operation.</summary>
    string? PlayerName { get; }

    /// <summary>Gets the machine identity, or null when no machine started the operation.</summary>
    string? MachineId { get; }
}

/// <summary>An operation started by a player.</summary>
public sealed class PlayerSource : IActionSource
{
    /// <summary>Creates a player source.</summary>
    public PlayerSource(string playerName, string? machineId = null)
    {
        PlayerName = string.IsNullOrEmpty(playerName) ? throw new ArgumentException("A player name is required.", nameof(playerName)) : playerName;
        MachineId = machineId;
    }

    /// <inheritdoc />
    public string? PlayerName { get; }

    /// <inheritdoc />
    public string? MachineId { get; }
}

/// <summary>An operation started by a machine.</summary>
public sealed class MachineSource : IActionSource
{
    /// <summary>Creates a machine source.</summary>
    public MachineSource(string machineId)
    {
        MachineId = string.IsNullOrEmpty(machineId) ? throw new ArgumentException("A machine identity is required.", nameof(machineId)) : machineId;
    }

    /// <inheritdoc />
    public string? PlayerName => null;

    /// <inheritdoc />
    public string? MachineId { get; }
}

/// <summary>The after-load storage format migration; it raises no change notifications.</summary>
public sealed class MigrationSource : IActionSource
{
    private MigrationSource()
    {
    }

    /// <summary>Gets the migration source.</summary>
    public static MigrationSource Instance { get; } = new();

    /// <inheritdoc />
    public string? PlayerName => null;

    /// <inheritdoc />
    public string? MachineId => null;
}
