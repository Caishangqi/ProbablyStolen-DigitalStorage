using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Items;

/// <summary>Identifies an item definition and its persistent state.</summary>
public sealed class ItemKey : IEquatable<ItemKey>, IComparable<ItemKey>
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _components;
    private readonly byte[]? _canonicalState;
    private readonly int _hashCode;

    /// <summary>Creates an item key from a stable identifier and canonical state components.</summary>
    public ItemKey(string identifier, IEnumerable<KeyValuePair<string, string>>? components = null)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("An item identifier is required.", nameof(identifier));
        }

        Identifier = identifier;
        var ordered = (components ?? Array.Empty<KeyValuePair<string, string>>())
            .Select(component =>
            {
                if (string.IsNullOrEmpty(component.Key))
                {
                    throw new ArgumentException("A state component name is required.", nameof(components));
                }

                return new KeyValuePair<string, string>(component.Key, component.Value ?? string.Empty);
            })
            .OrderBy(component => component.Key, StringComparer.Ordinal)
            .ToArray();

        for (var index = 1; index < ordered.Length; index++)
        {
            if (string.Equals(ordered[index - 1].Key, ordered[index].Key, StringComparison.Ordinal))
            {
                throw new ArgumentException("State component names must be unique.", nameof(components));
            }
        }

        _components = new ReadOnlyCollection<KeyValuePair<string, string>>(ordered);
        _hashCode = ComputeHashCode();
    }

    private ItemKey(string identifier, byte[] canonicalState)
    {
        Identifier = identifier;
        _components = Array.Empty<KeyValuePair<string, string>>();
        _canonicalState = canonicalState;
        _hashCode = ComputeHashCode();
    }

    /// <summary>Creates a version 1 identity after validating and copying its canonical state.</summary>
    public static ItemKey FromCanonical(string identifier, byte[] canonicalState)
    {
        if (canonicalState == null) throw new ArgumentNullException(nameof(canonicalState));
        if (canonicalState.Length > 1048576)
        {
            throw new ArgumentException("The canonical identity exceeds the size limit.", nameof(canonicalState));
        }

        var copied = (byte[])canonicalState.Clone();
        if (!CanonicalIdentityValidator.IsValid(copied, identifier))
        {
            throw new ArgumentException("The canonical identity is invalid or unsupported.", nameof(canonicalState));
        }

        return new ItemKey(identifier, copied);
    }

    /// <summary>
    /// Gets what the canonical state records about the root item, or null for a legacy identity. The state was
    /// validated on creation, so a canonical identity always yields a summary.
    /// </summary>
    public CanonicalItemSummary? ReadSummary() =>
        _canonicalState == null ? null : CanonicalIdentityValidator.ReadSummary(_canonicalState, Identifier);

    /// <summary>Gets whether this identity uses the versioned canonical representation.</summary>
    public bool IsCanonical => _canonicalState != null;

    /// <summary>Gets the canonical schema version, or zero for the legacy representation.</summary>
    public int IdentityVersion => IsCanonical ? 1 : 0;

    /// <summary>Returns a detached copy of the version 1 state.</summary>
    public byte[] CopyCanonicalState() => _canonicalState == null
        ? throw new InvalidOperationException("A legacy identity has no canonical state.")
        : (byte[])_canonicalState.Clone();

    /// <summary>Gets the stable item definition identifier.</summary>
    public string Identifier { get; }

    /// <summary>Gets legacy state components; versioned identities do not expose components.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Components => _canonicalState != null
        ? throw new InvalidOperationException("A versioned identity has no legacy components.")
        : _components;

    /// <summary>Creates a key from an item identifier and one canonical state component.</summary>
    public static ItemKey Of(string identifier, string componentName, string componentValue) =>
        new(identifier, new[] { new KeyValuePair<string, string>(componentName, componentValue) });

    /// <summary>Gets the primary identity used for ordinal indexing.</summary>
    public string GetPrimaryKey() => Identifier;

    /// <summary>Checks whether another key has the same identifier and complete state.</summary>
    public bool Equals(ItemKey? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!string.Equals(Identifier, other.Identifier, StringComparison.Ordinal) ||
            IsCanonical != other.IsCanonical)
        {
            return false;
        }

        if (_canonicalState != null)
        {
            return _canonicalState.SequenceEqual(other._canonicalState!);
        }

        if (_components.Count != other._components.Count)
        {
            return false;
        }

        for (var index = 0; index < _components.Count; index++)
        {
            var left = _components[index];
            var right = other._components[index];
            if (!string.Equals(left.Key, right.Key, StringComparison.Ordinal) ||
                !string.Equals(left.Value, right.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Orders identities by ordinal identifier and complete component pairs.</summary>
    public int CompareTo(ItemKey? other)
    {
        if (other is null)
        {
            return 1;
        }

        var comparison = StringComparer.Ordinal.Compare(Identifier, other.Identifier);
        if (comparison != 0)
        {
            return comparison;
        }

        if (IsCanonical != other.IsCanonical)
        {
            return IsCanonical ? 1 : -1;
        }

        if (_canonicalState != null)
        {
            var otherState = other._canonicalState!;
            var commonLength = Math.Min(_canonicalState.Length, otherState.Length);
            for (var index = 0; index < commonLength; index++)
            {
                comparison = _canonicalState[index].CompareTo(otherState[index]);
                if (comparison != 0) return comparison;
            }

            return _canonicalState.Length.CompareTo(otherState.Length);
        }

        var commonCount = Math.Min(_components.Count, other._components.Count);
        for (var index = 0; index < commonCount; index++)
        {
            comparison = StringComparer.Ordinal.Compare(_components[index].Key, other._components[index].Key);
            if (comparison == 0)
            {
                comparison = StringComparer.Ordinal.Compare(_components[index].Value, other._components[index].Value);
            }

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return _components.Count.CompareTo(other._components.Count);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ItemKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _hashCode;

    /// <summary>Compares two item identities.</summary>
    public static bool operator ==(ItemKey? left, ItemKey? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    /// <summary>Compares two item identities.</summary>
    public static bool operator !=(ItemKey? left, ItemKey? right) => !(left == right);

    private int ComputeHashCode()
    {
        var hash = new HashCode();
        hash.Add(Identifier, StringComparer.Ordinal);
        if (_canonicalState != null)
        {
            hash.Add(1);
            foreach (var value in _canonicalState) hash.Add(value);
            return hash.ToHashCode();
        }

        foreach (var component in _components)
        {
            hash.Add(component.Key, StringComparer.Ordinal);
            hash.Add(component.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
