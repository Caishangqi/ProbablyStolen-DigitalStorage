using System;

namespace ProbablyStolen.DigitalStorage.Api.Networking.Security;

/// <summary>Who started a storage operation; used for logs and later permissions.</summary>
public interface IActionSource
{
    /// <summary>Gets the player name, or null when no player started the operation.</summary>
    string? PlayerName { get; }

    /// <summary>Gets the machine identity, or null when no machine started the operation.</summary>
    string? MachineId { get; }

    /// <summary>Gets the id of the mod or integration that started the operation, or null.</summary>
    string? ModId { get; }
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

    /// <inheritdoc />
    public string? ModId => null;
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

    /// <inheritdoc />
    public string? ModId => null;
}

/// <summary>An operation started by another mod or an integration plugin acting for one.</summary>
public sealed class ModSource : IActionSource
{
    /// <summary>Creates a mod source.</summary>
    public ModSource(string modId)
    {
        ModId = string.IsNullOrEmpty(modId) ? throw new ArgumentException("A mod id is required.", nameof(modId)) : modId;
    }

    /// <inheritdoc />
    public string? PlayerName => null;

    /// <inheritdoc />
    public string? MachineId => null;

    /// <inheritdoc />
    public string? ModId { get; }
}

/// <summary>Creates action sources for storage operations.</summary>
public static class ActionSource
{
    /// <summary>Gets a source that names nobody.</summary>
    public static IActionSource Empty { get; } = new EmptySource();

    /// <summary>Creates a player source.</summary>
    public static IActionSource OfPlayer(string playerName, string? machineId = null) => new PlayerSource(playerName, machineId);

    /// <summary>Creates a machine source.</summary>
    public static IActionSource OfMachine(string machineId) => new MachineSource(machineId);

    /// <summary>Creates a mod source.</summary>
    public static IActionSource OfMod(string modId) => new ModSource(modId);

    private sealed class EmptySource : IActionSource
    {
        public string? PlayerName => null;

        public string? MachineId => null;

        public string? ModId => null;
    }
}
