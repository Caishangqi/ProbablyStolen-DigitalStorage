using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Stages entries for a registry before creating its mutable registry instance.</summary>
public sealed class RegistryBuilder<T>
{
    private readonly Dictionary<Identifier, T> _entries = new();
    private readonly Identifier _registryId;

    /// <summary>Creates a builder for a named registry.</summary>
    /// <param name="registryId">The registry identifier.</param>
    public RegistryBuilder(Identifier registryId)
    {
        if (!registryId.IsValid)
            throw new ArgumentException("A registry requires a valid identifier.", nameof(registryId));
        _registryId = registryId;
    }

    /// <summary>Creates a builder for a named registry.</summary>
    /// <param name="registryId">The registry identifier.</param>
    /// <returns>A new builder.</returns>
    public static RegistryBuilder<T> Create(Identifier registryId) => new(registryId);

    /// <summary>Gets the registry identity used by <see cref="Build()"/>.</summary>
    public Identifier RegistryId => _registryId;

    /// <summary>Adds an entry to the pending registry.</summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="value">The entry value.</param>
    /// <returns>This builder.</returns>
    public RegistryBuilder<T> Add(Identifier id, T value)
    {
        if (!id.IsValid)
            throw new ArgumentException("A registry entry requires a valid identifier.", nameof(id));
        if (value is null)
            throw new ArgumentNullException(nameof(value));
        if (!_entries.TryAdd(id, value))
            throw new InvalidOperationException("Duplicate registry identifier: " + id);
        return this;
    }

    /// <summary>Builds the mutable registry from the pending entries.</summary>
    /// <returns>A registry containing the pending entries.</returns>
    public Registry<T> Build()
    {
        var registry = new Registry<T>(_registryId);
        foreach (var entry in _entries)
            registry.Register(entry.Key, entry.Value);
        return registry;
    }
}
