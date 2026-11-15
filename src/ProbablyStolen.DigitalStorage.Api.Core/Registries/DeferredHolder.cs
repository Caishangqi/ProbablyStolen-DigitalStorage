using System;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Resolves a deferred typed entry through its owning registry.</summary>
/// <typeparam name="T">The registry base value type.</typeparam>
/// <typeparam name="TDerived">The concrete value type produced by the factory.</typeparam>
public sealed class DeferredHolder<T, TDerived> where TDerived : T
{
    private Registry<T>? _registry;
    private bool _invalidated;
    private bool _disposed;

    internal DeferredHolder(ResourceKey<T> key) => Key = key;

    /// <summary>Gets the resource key of this deferred entry.</summary>
    public ResourceKey<T> Key { get; }

    /// <summary>Gets the registered value, resolving it from the current registry snapshot.</summary>
    /// <exception cref="InvalidOperationException">Thrown before publication or after a failed registration.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after the owning manager is disposed.</exception>
    public TDerived Value
    {
        get
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeferredHolder<T, TDerived>));
            if (_invalidated) throw new InvalidOperationException("Deferred holder is invalid: " + Key);
            if (_registry == null) throw new InvalidOperationException("Deferred holder is not bound: " + Key);
            return (TDerived)_registry.GetRequired(Key.EntryId)!;
        }
    }

    internal void Bind(Registry<T> registry)
    {
        if (_invalidated) throw new InvalidOperationException("Deferred holder cannot be bound: " + Key);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    internal void Invalidate()
    {
        _invalidated = true;
        _registry = null;
    }

    internal void Dispose() => _disposed = true;
}
