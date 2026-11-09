using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Immutable connection state used by the terminal menu and view.</summary>
public sealed class LinkState : IEquatable<LinkState>
{
    private LinkState(bool isConnected, string? description)
    {
        _isConnected = isConnected;
        _description = description;
    }

    private readonly bool _isConnected;
    private readonly string? _description;

    /// <summary>Creates the connected state.</summary>
    public static LinkState Connected() => new(true, null);

    /// <summary>Creates an offline state with an optional explanation key.</summary>
    public static LinkState Disconnected(string? description = null) => new(false, description);

    /// <summary>Gets whether the current local snapshot allows network operations.</summary>
    public bool IsConnected => _isConnected;

    /// <summary>Gets a localization key explaining an offline state, when available.</summary>
    public string? StatusDescription => _description;

    /// <inheritdoc />
    public bool Equals(LinkState? other) => other != null && _isConnected == other._isConnected &&
        string.Equals(_description, other._description, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LinkState other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_isConnected, _description);
}
