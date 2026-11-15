using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Collects entries for one atomic registration phase.</summary>
public sealed class RegisterEvent
{
    private readonly Action<RegisterEvent, Identifier, Identifier, Type, object, Action<object>, Action> _stage;
    private bool _active = true;

    internal RegisterEvent(Action<RegisterEvent, Identifier, Identifier, Type, object, Action<object>, Action> stage) => _stage = stage;

    internal List<PendingEntry> Entries { get; } = new();
    internal void Close() => _active = false;

    /// <summary>Registers a value in a named registry.</summary>
    /// <typeparam name="T">The registry value type.</typeparam>
    /// <param name="registryId">The target registry identifier.</param>
    /// <param name="entryId">The entry identifier.</param>
    /// <param name="value">The entry value.</param>
    public void Register<T>(Identifier registryId, Identifier entryId, T value) where T : notnull
    {
        EnsureActive();
        _stage(this, registryId, entryId, typeof(T), value, static _ => { }, static () => { });
    }

    internal void RegisterDeferred<T, TDerived>(Identifier registryId, Identifier entryId, TDerived value,
        Action<Registry<T>> bind, Action invalidate) where TDerived : T
    {
        EnsureActive();
        _stage(this, registryId, entryId, typeof(T), value!, valueObject => bind((Registry<T>)valueObject), invalidate);
    }

    private void EnsureActive()
    {
        if (!_active) throw new InvalidOperationException("Registry registration callback has ended.");
    }

    internal sealed class PendingEntry
    {
        internal PendingEntry(Identifier registryId, Identifier entryId, Type valueType, object value,
            Action<object> bind, Action invalidate)
        {
            RegistryId = registryId;
            EntryId = entryId;
            ValueType = valueType;
            Value = value;
            Bind = bind;
            Invalidate = invalidate;
        }

        internal Identifier RegistryId { get; }
        internal Identifier EntryId { get; }
        internal Type ValueType { get; }
        internal object Value { get; }
        internal Action<object> Bind { get; }
        internal Action Invalidate { get; }
    }
}
