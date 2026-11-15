using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Owns registries and their one-time creation, data registration, and freeze lifecycle.</summary>
public sealed class RegistryManager : IDisposable
{
    private readonly Dictionary<Identifier, Type> _types = new();
    private readonly Dictionary<Identifier, object> _registries = new();
    private readonly Dictionary<Identifier, (Type ValueType, Func<string, object> Decoder)> _decoders = new();
    private readonly List<RegisterEvent.PendingEntry> _pendingEntries = new();
    private readonly List<Action> _holderDisposals = new();
    private readonly List<Action<NewRegistryEvent>> _newRegistryCallbacks = new();
    private readonly List<Action<RegisterEvent>> _registerCallbacks = new();
    private readonly List<Action<DataPackRegistryEvent>> _dataCallbacks = new();
    private bool _created;
    private bool _registered;
    private bool _dataRegistered;
    private bool _callbacksClosed;
    private bool _failed;
    private bool _disposed;

    /// <summary>Registers a callback for the registry declaration phase.</summary>
    public event Action<NewRegistryEvent> NewRegistry
    {
        add { EnsureCanSubscribe(); _newRegistryCallbacks.Add(value ?? throw new ArgumentNullException(nameof(value))); }
        remove { _newRegistryCallbacks.Remove(value!); }
    }

    /// <summary>Registers a callback for the code entry phase.</summary>
    public event Action<RegisterEvent> Register
    {
        add { EnsureCanSubscribe(); _registerCallbacks.Add(value ?? throw new ArgumentNullException(nameof(value))); }
        remove { _registerCallbacks.Remove(value!); }
    }

    /// <summary>Registers a callback for data registry and decoder declarations.</summary>
    public event Action<DataPackRegistryEvent> DataPackRegistry
    {
        add { EnsureCanSubscribe(); _dataCallbacks.Add(value ?? throw new ArgumentNullException(nameof(value))); }
        remove { _dataCallbacks.Remove(value!); }
    }

    /// <summary>Gets whether all declarations completed and registries are frozen.</summary>
    public bool IsFrozen { get { EnsureAlive(); return _registered && _dataRegistered; } }

    /// <summary>Gets declared registry identifiers in deterministic order.</summary>
    public IReadOnlyList<Identifier> RegistryIds
    {
        get { EnsureAlive(); return Array.AsReadOnly(_types.Keys.OrderBy(id => id).ToArray()); }
    }

    /// <summary>Closes callback subscription while leaving the declared phases runnable.</summary>
    public void CloseCallbacks() { EnsureAlive(); _callbacksClosed = true; }

    /// <summary>Runs registry creation callbacks exactly once.</summary>
    public void CreateRegistries()
    {
        EnsurePhaseAvailable();
        if (_created) throw new InvalidOperationException("Registry creation phase has already run.");
        try
        {
            var declarations = new List<(Identifier Id, Type Type, Func<Identifier, object> Factory)>();
            var callback = new NewRegistryEvent((id, type, factory) =>
            {
                if (!id.IsValid || _types.ContainsKey(id) || declarations.Any(item => item.Id == id))
                    throw new InvalidOperationException("Duplicate or invalid registry identifier: " + id);
                declarations.Add((id, type, factory));
            });
            try
            {
                foreach (var subscriber in _newRegistryCallbacks.ToArray()) subscriber(callback);
            }
            finally { callback.Close(); }
            foreach (var declaration in declarations)
            {
                _types.Add(declaration.Id, declaration.Type);
                _registries.Add(declaration.Id, declaration.Factory(declaration.Id));
            }
            _created = true;
        }
        catch
        {
            Fail();
            throw;
        }
    }

    /// <summary>Runs code registration callbacks as one atomic entry batch.</summary>
    public void RegisterEntries()
    {
        EnsurePhaseAvailable();
        if (!_created || _registered) throw new InvalidOperationException("Registry creation must complete before registration.");
        var staged = new RegisterEvent((owner, registryId, entryId, type, value, bind, invalidate) =>
        {
            if (!_types.TryGetValue(registryId, out var registryType) || registryType != type)
                throw new InvalidOperationException("Registry is missing or has a different value type: " + registryId);
            if (!entryId.IsValid) throw new ArgumentException("A registry entry requires a valid identifier.", nameof(entryId));
            if (value == null) throw new ArgumentNullException(nameof(value));
            owner.Entries.Add(new RegisterEvent.PendingEntry(registryId, entryId, type, value, bind, invalidate));
        });
        try
        {
            foreach (var subscriber in _registerCallbacks.ToArray()) subscriber(staged);
        }
        catch
        {
            foreach (var entry in staged.Entries) entry.Invalidate();
            Fail();
            throw;
        }
        finally { staged.Close(); }
        try
        {
            var duplicates = new HashSet<(Identifier Registry, Identifier Entry)>();
            foreach (var entry in staged.Entries)
            {
                var registry = (IRegistryLifecycle)_registries[entry.RegistryId];
                if (!duplicates.Add((entry.RegistryId, entry.EntryId)) || registry.Contains(entry.EntryId))
                    throw new InvalidOperationException("Duplicate registry identifier: " + entry.EntryId);
            }
            foreach (var entry in staged.Entries)
                ((IRegistryLifecycle)_registries[entry.RegistryId]).Add(entry.EntryId, entry.Value);
            foreach (var entry in staged.Entries)
                entry.Bind(_registries[entry.RegistryId]);
            _registered = true;
        }
        catch
        {
            foreach (var entry in staged.Entries) entry.Invalidate();
            Fail();
            throw;
        }
    }

    /// <summary>Runs data registry declarations and exposes registered decoders.</summary>
    public void RegisterDataRegistries()
    {
        EnsurePhaseAvailable();
        if (!_registered || _dataRegistered) throw new InvalidOperationException("Code registration must complete before data registry registration.");
        try
        {
            var declarations = new List<(Identifier Id, Type Type, Func<string, object> Decoder)>();
            var callback = new DataPackRegistryEvent((id, type, decoder) =>
            {
                if (!id.IsValid || !_types.TryGetValue(id, out var registeredType) || registeredType != type || _decoders.ContainsKey(id)
                    || declarations.Any(item => item.Id == id))
                    throw new InvalidOperationException("Data registry is missing, duplicated, or has a different type: " + id);
                declarations.Add((id, type, decoder));
            });
            try
            {
                foreach (var subscriber in _dataCallbacks.ToArray()) subscriber(callback);
            }
            finally { callback.Close(); }
            foreach (var declaration in declarations) _decoders.Add(declaration.Id, (declaration.Type, declaration.Decoder));
            foreach (var registry in _registries.Values) ((IRegistryLifecycle)registry).Freeze();
            _dataRegistered = true;
        }
        catch
        {
            Fail();
            throw;
        }
    }

    /// <summary>Decodes a data value using the decoder registered for its registry.</summary>
    /// <typeparam name="T">The data value type.</typeparam>
    /// <param name="registryId">The registry identifier.</param>
    /// <param name="data">The serialized data.</param>
    /// <returns>The decoded value.</returns>
    public T Decode<T>(Identifier registryId, string data)
    {
        EnsureAlive();
        if (!_dataRegistered || !_decoders.TryGetValue(registryId, out var decoder) || decoder.ValueType != typeof(T))
            throw new InvalidOperationException("No data decoder is available for registry: " + registryId);
        return (T)decoder.Decoder(data);
    }

    /// <summary>Gets a typed registry.</summary>
    /// <typeparam name="T">The registry value type.</typeparam>
    /// <param name="id">The registry identifier.</param>
    /// <returns>The owned registry.</returns>
    public Registry<T> Get<T>(Identifier id)
    {
        EnsureAlive();
        if (!_registries.TryGetValue(id, out var registry) || registry is not Registry<T> typed)
            throw new KeyNotFoundException("Registry is unavailable or has a different type: " + id);
        return typed;
    }

    /// <summary>Freezes all registries after all declared callback phases complete.</summary>
    public void Freeze()
    {
        EnsureAlive();
        if (!_registered || !_dataRegistered) throw new InvalidOperationException("All registry phases must complete before freezing.");
        foreach (var registry in _registries.Values) ((IRegistryLifecycle)registry).Freeze();
    }

    /// <summary>Invalidates registries and holders that resolve through them.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var entry in _pendingEntries) entry.Invalidate();
        _pendingEntries.Clear();
        foreach (var dispose in _holderDisposals) dispose();
        _holderDisposals.Clear();
        foreach (var registry in _registries.Values) ((IRegistryLifecycle)registry).Invalidate();
        _registries.Clear();
        _types.Clear();
        _decoders.Clear();
        _newRegistryCallbacks.Clear();
        _registerCallbacks.Clear();
        _dataCallbacks.Clear();
        _disposed = true;
    }

    internal IReadOnlyList<Declaration> StageDeclarations()
    {
        EnsurePhaseAvailable();
        if (_created) throw new InvalidOperationException("Registry creation phase has already run.");
        _created = true;
        var declarations = new List<Declaration>();
        var phase = new NewRegistryEvent((id, type, factory) =>
        {
            if (!id.IsValid || declarations.Any(value => value.Id == id))
                throw new InvalidOperationException("Duplicate or invalid registry identifier: " + id);
            declarations.Add(new Declaration(id, type, factory));
        });
        try { foreach (var callback in _newRegistryCallbacks.ToArray()) callback(phase); }
        catch { Fail(); throw; }
        finally { phase.Close(); }
        return declarations;
    }

    internal IReadOnlyList<RegisterEvent.PendingEntry> StageEntries(IReadOnlyDictionary<Identifier, Type> types)
    {
        EnsurePhaseAvailable();
        if (!_created || _registered) throw new InvalidOperationException("Registry creation must complete before registration.");
        _registered = true;
        var phase = new RegisterEvent((owner, registryId, entryId, type, value, bind, invalidate) =>
        {
            if (!types.TryGetValue(registryId, out var declared) || declared != type)
            {
                invalidate();
                throw new InvalidOperationException("Registry is missing or has a different value type: " + registryId);
            }
            if (!entryId.IsValid || value == null)
            {
                invalidate();
                throw new ArgumentException("A registry entry requires an identifier and value.");
            }
            var entry = new RegisterEvent.PendingEntry(registryId, entryId, type, value, bind, invalidate);
            owner.Entries.Add(entry);
            _pendingEntries.Add(entry);
        });
        try { foreach (var callback in _registerCallbacks.ToArray()) callback(phase); }
        catch
        {
            foreach (var entry in _pendingEntries) entry.Invalidate();
            Fail();
            throw;
        }
        finally { phase.Close(); }
        return phase.Entries;
    }

    internal IReadOnlyList<DataDeclaration> StageDataDeclarations()
    {
        EnsurePhaseAvailable();
        if (!_registered || _dataRegistered) throw new InvalidOperationException("Code registration must complete before data registry registration.");
        _dataRegistered = true;
        var declarations = new List<DataDeclaration>();
        var phase = new DataPackRegistryEvent((id, type, decoder) =>
        {
            if (!id.IsValid || declarations.Any(value => value.Id == id))
                throw new InvalidOperationException("Duplicate or invalid data registry: " + id);
            declarations.Add(new DataDeclaration(id, type, decoder));
        });
        try { foreach (var callback in _dataCallbacks.ToArray()) callback(phase); }
        catch { Fail(); throw; }
        finally { phase.Close(); }
        return declarations;
    }

    internal void TrackHolder(Action dispose) { EnsurePhaseAvailable(); _holderDisposals.Add(dispose); }

    internal void ReleaseStagedEntries() { _pendingEntries.Clear(); _holderDisposals.Clear(); }

    internal void PublishStaged(IEnumerable<Declaration> declarations, IEnumerable<RegisterEvent.PendingEntry> entries,
        IEnumerable<DataDeclaration> decoders)
    {
        EnsurePhaseAvailable();
        if (_created) throw new InvalidOperationException("Registry publication has already started.");
        _created = true;
        var pending = entries.ToArray();
        try
        {
            foreach (var declaration in declarations)
            {
                _types.Add(declaration.Id, declaration.ValueType);
                _registries.Add(declaration.Id, declaration.Create(declaration.Id));
            }
            foreach (var decoder in decoders)
            {
                if (!_types.TryGetValue(decoder.Id, out var type) || type != decoder.ValueType)
                    throw new InvalidOperationException("Data registry has a different value type: " + decoder.Id);
                _decoders.Add(decoder.Id, (decoder.ValueType, decoder.Decode));
            }
            foreach (var entry in pending)
            {
                if (!_types.TryGetValue(entry.RegistryId, out var type) || type != entry.ValueType)
                    throw new InvalidOperationException("Registry has a different value type: " + entry.RegistryId);
                ((IRegistryLifecycle)_registries[entry.RegistryId]).Add(entry.EntryId, entry.Value);
            }
            foreach (var registry in _registries.Values) ((IRegistryLifecycle)registry).Freeze();
            _registered = _dataRegistered = true;
            foreach (var entry in pending) entry.Bind(_registries[entry.RegistryId]);
        }
        catch
        {
            foreach (var entry in pending) entry.Invalidate();
            Fail();
            throw;
        }
    }

    internal sealed class Declaration
    {
        internal Declaration(Identifier id, Type valueType, Func<Identifier, object> create)
        { Id = id; ValueType = valueType; Create = create; }
        internal Identifier Id { get; }
        internal Type ValueType { get; }
        internal Func<Identifier, object> Create { get; }
    }

    internal sealed class DataDeclaration
    {
        internal DataDeclaration(Identifier id, Type valueType, Func<string, object> decode)
        { Id = id; ValueType = valueType; Decode = decode; }
        internal Identifier Id { get; }
        internal Type ValueType { get; }
        internal Func<string, object> Decode { get; }
    }

    private void EnsureCanSubscribe() { EnsurePhaseAvailable(); if (_created || _callbacksClosed) throw new InvalidOperationException("Registry callbacks are closed."); }
    private void EnsurePhaseAvailable() { EnsureAlive(); if (_failed) throw new InvalidOperationException("Registry lifecycle is unavailable after a failed phase."); }
    private void EnsureAlive() { if (_disposed) throw new ObjectDisposedException(nameof(RegistryManager)); }
    private void Fail() { _failed = true; foreach (var registry in _registries.Values) ((IRegistryLifecycle)registry).Invalidate(); _registries.Clear(); }
}
