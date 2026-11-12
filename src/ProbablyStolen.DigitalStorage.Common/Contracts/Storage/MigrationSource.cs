using ProbablyStolen.DigitalStorage.Api.Networking.Security;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

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

    /// <inheritdoc />
    public string? ModId => null;
}
