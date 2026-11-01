using System;

namespace ProbablyStolen.DigitalStorage.Common.Initialization;

/// <summary>Identifies a module using its stable loader identifier and display name.</summary>
public sealed class ModuleIdentity : IEquatable<ModuleIdentity>
{
    private ModuleIdentity(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    /// <summary>Gets the stable loader identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the name shown to players and in diagnostics.</summary>
    public string DisplayName { get; }

    /// <summary>Creates an identity when the identifier and display name are valid.</summary>
    /// <param name="id">The loader identifier.</param>
    /// <param name="displayName">The displayed module name.</param>
    /// <param name="identity">The created identity, or null when validation fails.</param>
    /// <param name="error">A validation reason, or null when creation succeeds.</param>
    /// <returns>True when an identity was created.</returns>
    public static bool TryCreate(string? id, string? displayName, out ModuleIdentity? identity, out string? error)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id[0] == '.' || !ContainsOnlySupportedCharacters(id))
        {
            identity = null;
            error = "The module identifier is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            identity = null;
            error = "The module display name is required.";
            return false;
        }

        identity = new ModuleIdentity(id, displayName);
        error = null;
        return true;
    }

    /// <inheritdoc />
    public bool Equals(ModuleIdentity? other)
    {
        return other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ModuleIdentity other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Id);
    }

    private static bool ContainsOnlySupportedCharacters(string id)
    {
        for (var index = 0; index < id.Length; index++)
        {
            var character = id[index];
            var isLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            var isPunctuation = character is '_' or '-' || (index > 0 && character == '.');
            if (!isLetter && !isDigit && !isPunctuation)
            {
                return false;
            }
        }

        return true;
    }
}
