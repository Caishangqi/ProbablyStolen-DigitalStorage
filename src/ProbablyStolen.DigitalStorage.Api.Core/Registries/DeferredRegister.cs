using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Collects factories for one namespace and submits them during registry registration.</summary>
/// <typeparam name="T">The registry base value type.</typeparam>
public sealed class DeferredRegister<T>
{
    private readonly Identifier _registryId;
    private readonly RegistryManager _manager;
    private readonly string _namespaceName;
    private readonly List<Registration> _registrations = new();
    private readonly HashSet<Identifier> _ids = new();
    private readonly RegistryRegistration? _scope;
    private bool _submitted;

    private DeferredRegister(RegistryManager manager, Identifier registryId, string namespaceName, RegistryRegistration? scope = null)
    {
        if (manager == null) throw new ArgumentNullException(nameof(manager));
        if (!registryId.IsValid) throw new ArgumentException("A deferred register requires a valid registry identifier.", nameof(registryId));
        if (!Identifier.TryParse(namespaceName + ":entry", out var namespaceCheck))
            throw new ArgumentException("The namespace must be lowercase identifier text.", nameof(namespaceName));
        _manager = manager;
        _registryId = registryId;
        _namespaceName = namespaceCheck.Namespace;
        _scope = scope;
        manager.Register += Submit;
    }

    /// <summary>Creates and attaches this typed deferred register to a manager.</summary>
    /// <param name="manager">The composition-owned registry manager.</param>
    /// <param name="registryId">The target registry identifier.</param>
    /// <param name="namespaceName">The namespace used for entry identifiers.</param>
    /// <returns>A deferred register.</returns>
    public static DeferredRegister<T> Create(RegistryManager manager, Identifier registryId, string namespaceName)
        => new(manager, registryId, namespaceName);

    /// <summary>Creates and attaches a deferred register using the registry namespace for entries.</summary>
    /// <param name="manager">The composition-owned registry manager.</param>
    /// <param name="registryId">The target registry identifier.</param>
    /// <returns>A deferred register.</returns>
    public static DeferredRegister<T> Create(RegistryManager manager, Identifier registryId)
        => new(manager, registryId, registryId.Namespace);

    /// <summary>Creates a deferred register attached to a scoped public facade.</summary>
    public static DeferredRegister<T> Create(RegistryRegistration registration, Identifier registryId, string namespaceName)
    {
        if (registration == null) throw new ArgumentNullException(nameof(registration));
        registration.EnsureOpen();
        return new(registration.Manager, registryId, namespaceName, registration);
    }

    /// <summary>Creates a deferred register for the registry named by <paramref name="registryKey"/>.</summary>
    /// <param name="registration">The scoped registration facade of the contributing batch.</param>
    /// <param name="registryKey">The target registry key, such as a <c>DigitalStorageRegistries</c> key.</param>
    /// <param name="namespaceName">The namespace of the registered entries, normally the contributing mod id.</param>
    /// <returns>A deferred register.</returns>
    public static DeferredRegister<T> Create(RegistryRegistration registration, ResourceKey<Registry<T>> registryKey, string namespaceName)
    {
        if (registryKey.RegistryId != ResourceKey.RootRegistryId)
            throw new ArgumentException("The key does not name a registry.", nameof(registryKey));
        return Create(registration, registryKey.EntryId, namespaceName);
    }

    /// <summary>Creates a deferred register using the facade registry namespace for entries.</summary>
    public static DeferredRegister<T> Create(RegistryRegistration registration, Identifier registryId)
    {
        if (registration == null) throw new ArgumentNullException(nameof(registration));
        registration.EnsureOpen();
        return new(registration.Manager, registryId, registryId.Namespace, registration);
    }

    /// <summary>Registers a concrete factory under a namespace-relative path.</summary>
    /// <typeparam name="TDerived">The concrete value type.</typeparam>
    /// <param name="path">The entry path.</param>
    /// <param name="factory">The factory evaluated once during registration.</param>
    /// <returns>A holder resolved after successful publication.</returns>
    public DeferredHolder<T, TDerived> Register<TDerived>(string path, Func<TDerived> factory) where TDerived : T
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        return Register<TDerived>(path, _ => factory());
    }

    /// <summary>Registers a concrete factory that receives the full entry identifier.</summary>
    /// <typeparam name="TDerived">The concrete value type.</typeparam>
    /// <param name="path">The entry path.</param>
    /// <param name="factory">The factory evaluated once during registration with the full entry identifier.</param>
    /// <returns>A holder resolved after successful publication.</returns>
    public DeferredHolder<T, TDerived> Register<TDerived>(string path, Func<Identifier, TDerived> factory) where TDerived : T
    {
        if (_scope != null) _scope.EnsureOpen();
        if (_submitted) throw new InvalidOperationException("Deferred registration was already submitted.");
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        var entryId = new Identifier(_namespaceName, path);
        if (!_ids.Add(entryId)) throw new InvalidOperationException("Duplicate deferred identifier: " + entryId);
        var holder = new DeferredHolder<T, TDerived>(new ResourceKey<T>(_registryId, entryId));
        _manager.TrackHolder(holder.Dispose);
        _registrations.Add(new Registration(holder.Key, () => factory(entryId)!, registry => holder.Bind(registry), holder.Invalidate));
        return holder;
    }

    private void Submit(RegisterEvent registrationEvent)
    {
        if (_submitted) throw new InvalidOperationException("Deferred registration was already submitted.");
        _submitted = true;
        try
        {
            foreach (var registration in _registrations)
            {
                var value = registration.Create();
                registrationEvent.RegisterDeferred<T, T>(_registryId, registration.Key.EntryId, value,
                    registry => registration.Bind(registry), registration.Invalidate);
            }
        }
        catch
        {
            foreach (var registration in _registrations) registration.Invalidate();
            throw;
        }
    }

    private sealed class Registration
    {
        internal Registration(ResourceKey<T> key, Func<T> create, Action<Registry<T>> bind, Action invalidate)
        {
            Key = key;
            Create = create;
            Bind = bind;
            Invalidate = invalidate;
        }

        internal ResourceKey<T> Key { get; }
        internal Func<T> Create { get; }
        internal Action<Registry<T>> Bind { get; }
        internal Action Invalidate { get; }
    }
}
