using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Identifies a registry entry with a lowercase namespace and path.</summary>
public readonly struct Identifier : IEquatable<Identifier>, IComparable<Identifier>
{
    private readonly string? _namespaceName;
    private readonly string? _path;

    /// <summary>Creates a validated identifier.</summary>
    /// <param name="namespaceName">The lowercase namespace.</param>
    /// <param name="path">The lowercase path, which may contain slash separated segments.</param>
    /// <exception cref="ArgumentException">Thrown when either component is invalid.</exception>
    public Identifier(string namespaceName, string path)
    {
        if (!IsPart(namespaceName) || !IsPath(path))
            throw new ArgumentException("Identifier components must be lowercase ASCII names.");

        _namespaceName = namespaceName;
        _path = path;
    }

    /// <summary>Gets the namespace, or an empty string for the default value.</summary>
    public string Namespace => _namespaceName ?? string.Empty;

    /// <summary>Gets the path, or an empty string for the default value.</summary>
    public string Path => _path ?? string.Empty;

    /// <summary>Gets whether this value was constructed from valid identifier components.</summary>
    public bool IsValid => _namespaceName != null && _path != null;

    /// <summary>Parses the namespace:path representation.</summary>
    /// <param name="value">The representation to parse.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="FormatException">Thrown when the representation is invalid.</exception>
    public static Identifier Parse(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        var separator = value.IndexOf(':');
        if (separator <= 0 || separator != value.LastIndexOf(':') || separator == value.Length - 1)
            throw new FormatException("Identifiers use namespace:path syntax.");

        try
        {
            return new Identifier(value.Substring(0, separator), value.Substring(separator + 1));
        }
        catch (ArgumentException error)
        {
            throw new FormatException("Identifier components are invalid.", error);
        }
    }

    /// <summary>Attempts to parse the namespace:path representation.</summary>
    /// <param name="value">The representation to parse.</param>
    /// <param name="identifier">The parsed identifier when successful.</param>
    /// <returns><see langword="true"/> when parsing succeeds.</returns>
    public static bool TryParse(string? value, out Identifier identifier)
    {
        if (value == null)
        {
            identifier = default;
            return false;
        }

        try
        {
            identifier = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            identifier = default;
            return false;
        }
    }

    /// <inheritdoc />
    public bool Equals(Identifier other) => string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
        && string.Equals(Path, other.Path, StringComparison.Ordinal)
        && IsValid == other.IsValid;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Identifier other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Namespace), StringComparer.Ordinal.GetHashCode(Path), IsValid);

    /// <inheritdoc />
    public int CompareTo(Identifier other)
    {
        var result = string.CompareOrdinal(Namespace, other.Namespace);
        return result != 0 ? result : string.CompareOrdinal(Path, other.Path);
    }

    /// <inheritdoc />
    public override string ToString() => IsValid ? Namespace + ":" + Path : string.Empty;

    /// <summary>Tests two identifiers for ordinal equality.</summary>
    public static bool operator ==(Identifier left, Identifier right) => left.Equals(right);

    /// <summary>Tests two identifiers for ordinal inequality.</summary>
    public static bool operator !=(Identifier left, Identifier right) => !left.Equals(right);

    private static bool IsPart(string value) => !string.IsNullOrEmpty(value) && value.All(IsNameCharacter);

    private static bool IsPath(string value) => !string.IsNullOrEmpty(value)
        && value.Split('/').All(part => part.Length > 0 && part.All(IsNameCharacter));

    private static bool IsNameCharacter(char value) => value >= 'a' && value <= 'z'
        || value >= '0' && value <= '9'
        || value == '_' || value == '-' || value == '.';
}
