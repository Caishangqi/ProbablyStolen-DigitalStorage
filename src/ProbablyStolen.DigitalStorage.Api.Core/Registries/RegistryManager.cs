using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Owns registries and their one-time creation, registration, and freeze lifecycle.</summary>
public sealed class RegistryManager : IDisposable
{
    private readonly Dictionary<Identifier, Type> _types = new();
    private readonly Dictionary<Identifier, object> _registries = new();
    private readonly List<RegisterEvent.PendingEntry> _pendingEntries = new();
    private readonly List<Action> _holderDisposals = new();
    private readonly List<Action<NewRegistryEvent>> _newRegistryCallbacks = new();
    private readonly List<Action<RegisterEvent>> _registerCallbacks = new();
    private bool _created;
    private bool _registered;
    private bool _frozen;
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

    /// <summary>Gets whether all declarations completed and registries are frozen.</summary>
    public bool IsFrozen { get { EnsureAlive(); return _frozen; } }

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

    /// <summary>Freezes all registries once creation and registration have completed.</summary>
    public void Freeze()
    {
        EnsurePhaseAvailable();
        if (!_registered) throw new InvalidOperationException("Registration must complete before freezing.");
        foreach (var registry in _registries.Values) ((IRegistryLifecycle)registry).Freeze();
        _frozen = true;
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
        _newRegistryCallbacks.Clear();
        _registerCallbacks.Clear();
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

    internal void TrackHolder(Action dispose) { EnsurePhaseAvailable(); _holderDisposals.Add(dispose); }

    internal void ReleaseStagedEntries() { _pendingEntries.Clear(); _holderDisposals.Clear(); }

    internal void PublishStaged(IEnumerable<Declaration> declarations, IEnumerable<RegisterEvent.PendingEntry> entries)
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
            foreach (var entry in pending)
            {
                if (!_types.TryGetValue(entry.RegistryId, out var type) || type != entry.ValueType)
                    throw new InvalidOperationException("Registry has a different value type: " + entry.RegistryId);
                ((IRegistryLifecycle)_registries[entry.RegistryId]).Add(entry.EntryId, entry.Value);
            }
            foreach (var registry in _registries.Values) ((IRegistryLifecycle)registry).Freeze();
            _registered = _frozen = true;
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

    private void EnsureCanSubscribe() { EnsurePhaseAvailable(); if (_created || _callbacksClosed) throw new InvalidOperationException("Registry callbacks are closed."); }
    private void EnsurePhaseAvailable() { EnsureAlive(); if (_failed) throw new InvalidOperationException("Registry lifecycle is unavailable after a failed phase."); }
    private void EnsureAlive() { if (_disposed) throw new ObjectDisposedException(nameof(RegistryManager)); }
    private void Fail() { _failed = true; foreach (var registry in _registries.Values) ((IRegistryLifecycle)registry).Invalidate(); _registries.Clear(); }
}
