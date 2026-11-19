using System;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Creates resource keys that name registries.</summary>
public static class ResourceKey
{
    /// <summary>Gets the identifier of the root registry that every registry key belongs to.</summary>
    public static readonly Identifier RootRegistryId = Identifier.Parse("digital_storage:root");

    /// <summary>Creates the key of the registry named <paramref name="registryId"/>.</summary>
    /// <typeparam name="T">The registry value type.</typeparam>
    /// <param name="registryId">The registry identifier.</param>
    /// <returns>A key whose <see cref="ResourceKey{T}.EntryId"/> is the registry identifier.</returns>
    public static ResourceKey<Registry<T>> CreateRegistryKey<T>(Identifier registryId)
    {
        if (!registryId.IsValid) throw new ArgumentException("A registry key requires a valid identifier.", nameof(registryId));
        return new ResourceKey<Registry<T>>(RootRegistryId, registryId);
    }
}

/// <summary>Identifies a typed entry by both registry and entry identifier.</summary>
public readonly struct ResourceKey<T> : IEquatable<ResourceKey<T>>
{
    /// <summary>Creates a resource key for a registry entry.</summary>
    /// <param name="registryId">The owning registry identifier.</param>
    /// <param name="entryId">The entry identifier.</param>
    public ResourceKey(Identifier registryId, Identifier entryId)
    {
        RegistryId = registryId;
        EntryId = entryId;
    }

    /// <summary>Gets the owning registry identifier.</summary>
    public Identifier RegistryId { get; }

    /// <summary>Gets the entry identifier.</summary>
    public Identifier EntryId { get; }

    /// <summary>Gets whether both identifiers are valid.</summary>
    public bool IsValid => RegistryId.IsValid && EntryId.IsValid;

    /// <inheritdoc />
    public bool Equals(ResourceKey<T> other) => RegistryId.Equals(other.RegistryId) && EntryId.Equals(other.EntryId);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ResourceKey<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(RegistryId, EntryId);

    /// <inheritdoc />
    public override string ToString() => RegistryId + "[" + EntryId + "]";

    /// <summary>Tests keys for equality.</summary>
    public static bool operator ==(ResourceKey<T> left, ResourceKey<T> right) => left.Equals(right);

    /// <summary>Tests keys for inequality.</summary>
    public static bool operator !=(ResourceKey<T> left, ResourceKey<T> right) => !left.Equals(right);
}
