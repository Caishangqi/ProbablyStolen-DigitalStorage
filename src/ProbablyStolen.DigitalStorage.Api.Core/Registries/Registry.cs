using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Read-only access to a typed registry.</summary>
public interface IReadOnlyRegistry<T>
{
    /// <summary>Gets entries in deterministic identifier order.</summary>
    IReadOnlyList<KeyValuePair<Identifier, T>> Entries { get; }
    /// <summary>Looks up an entry by identifier.</summary>
    bool TryGetValue(Identifier id, out T value);
    /// <summary>Gets an entry or throws when absent.</summary>
    T GetRequired(Identifier id);
}

internal interface IRegistryLifecycle
{
    Type ValueType { get; }
    bool Contains(Identifier id);
    void Add(Identifier id, object value);
    void Freeze();
    void Invalidate();
}


/// <summary>Provides mutable registration and immutable-after-freeze lookup for one typed registry.</summary>
public sealed class Registry<T> : IRegistryLifecycle, IReadOnlyRegistry<T>
{
    private readonly Dictionary<Identifier, T> _entries = new();
    private readonly List<KeyValuePair<Identifier, T>> _registrationOrder = new();
    private IReadOnlyList<KeyValuePair<Identifier, T>>? _orderedEntries;
    private bool _frozen;
    private bool _disposed;

    /// <summary>Creates an empty registry.</summary>
    /// <param name="registryId">The stable identifier owned by this registry.</param>
    public Registry(Identifier registryId)
    {
        if (!registryId.IsValid)
            throw new ArgumentException("A registry requires a valid identifier.", nameof(registryId));
        RegistryId = registryId;
    }

    /// <summary>Gets the stable identifier of this registry.</summary>
    public Identifier RegistryId { get; }

    /// <summary>Gets whether new entries are rejected because the registry is frozen.</summary>
    public bool IsFrozen { get { EnsureAlive(); return _frozen; } }

    /// <summary>Gets the number of registered entries.</summary>
    public int Count { get { EnsureAlive(); return _entries.Count; } }

    /// <summary>Gets entries in ordinal namespace and path order.</summary>
    public IReadOnlyList<KeyValuePair<Identifier, T>> Entries
    {
        get
        {
            EnsureAlive();
            return _orderedEntries ??= Array.AsReadOnly(_entries.OrderBy(pair => pair.Key).ToArray());
        }
    }

    /// <summary>Gets entries in registration order.</summary>
    public IReadOnlyList<KeyValuePair<Identifier, T>> RegistrationEntries
    {
        get
        {
            EnsureAlive();
            return Array.AsReadOnly(_registrationOrder.ToArray());
        }
    }

    /// <summary>Adds an entry and rejects invalid or duplicate identifiers.</summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="value">The value to publish.</param>
    /// <returns>This registry.</returns>
    public Registry<T> Register(Identifier id, T value)
    {
        EnsureWritable();
        ValidateEntry(id, value);
        if (!_entries.TryAdd(id, value))
            throw new InvalidOperationException("Duplicate registry identifier: " + id);
        _registrationOrder.Add(new KeyValuePair<Identifier, T>(id, value));
        _orderedEntries = null;
        return this;
    }

    /// <summary>Freezes the registry against further writes.</summary>
    public void Freeze() { EnsureAlive(); _frozen = true; }

    /// <summary>Attempts to retrieve an entry.</summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="value">The value when found.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool TryGetValue(Identifier id, out T value) { EnsureAlive(); return _entries.TryGetValue(id, out value!); }

    /// <summary>Retrieves an entry or throws when it is absent.</summary>
    /// <param name="id">The entry identifier.</param>
    /// <returns>The registered value.</returns>
    public T GetRequired(Identifier id)
    {
        EnsureAlive();
        if (!_entries.TryGetValue(id, out var value))
            throw new KeyNotFoundException("Unregistered entry: " + id);
        return value;
    }

    /// <summary>Retrieves an entry or returns the type default when it is absent.</summary>
    /// <param name="id">The entry identifier.</param>
    /// <returns>The registered value or <see langword="default"/>.</returns>
    public T? GetValueOrDefault(Identifier id) { EnsureAlive(); return _entries.TryGetValue(id, out var value) ? value : default; }

    /// <summary>Retrieves an entry or returns the supplied fallback.</summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="fallback">The value returned when the entry is absent.</param>
    /// <returns>The registered value or <paramref name="fallback"/>.</returns>
    public T GetOrDefault(Identifier id, T fallback = default!)
    {
        EnsureAlive();
        return _entries.TryGetValue(id, out var value) ? value : fallback;
    }

    /// <summary>Invalidates all access to the registry.</summary>
    internal void Invalidate() { _disposed = true; _entries.Clear(); _registrationOrder.Clear(); _orderedEntries = null; }

    internal void EnsureAlive() { if (_disposed) throw new ObjectDisposedException(nameof(Registry<T>)); }
    void IRegistryLifecycle.Add(Identifier id, object value) => Register(id, (T)value);
    bool IRegistryLifecycle.Contains(Identifier id) { EnsureAlive(); return _entries.ContainsKey(id); }
    Type IRegistryLifecycle.ValueType => typeof(T);
    void IRegistryLifecycle.Freeze() => Freeze();
    void IRegistryLifecycle.Invalidate() => Invalidate();

    private void EnsureWritable()
    {
        EnsureAlive();
        if (_frozen) throw new InvalidOperationException("Registry is frozen: " + RegistryId);
    }

    private static void ValidateEntry(Identifier id, T value)
    {
        if (!id.IsValid) throw new ArgumentException("A registry entry requires a valid identifier.", nameof(id));
        if (value is null) throw new ArgumentNullException(nameof(value));
    }
}
