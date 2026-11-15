using System;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Scoped public registration facade over a composition-owned registry manager.</summary>
public sealed class RegistryRegistration
{
    private readonly RegistryManager _manager;
    private bool _closed;

    /// <summary>Creates a facade over the manager owned by a composition root.</summary>
    public RegistryRegistration(RegistryManager manager) => _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    /// <summary>Gets whether this scoped registration facade accepts contributions.</summary>
    public bool IsOpen => !_closed;

    /// <summary>Registers a registry declaration callback while this scope is open.</summary>
    public event Action<NewRegistryEvent> NewRegistry
    {
        add { EnsureOpen(); _manager.NewRegistry += value; }
        remove { EnsureOpen(); _manager.NewRegistry -= value; }
    }

    /// <summary>Registers a code entry callback while this scope is open.</summary>
    public event Action<RegisterEvent> Register
    {
        add { EnsureOpen(); _manager.Register += value; }
        remove { EnsureOpen(); _manager.Register -= value; }
    }

    /// <summary>Registers a data registry callback while this scope is open.</summary>
    public event Action<DataPackRegistryEvent> DataPackRegistry
    {
        add { EnsureOpen(); _manager.DataPackRegistry += value; }
        remove { EnsureOpen(); _manager.DataPackRegistry -= value; }
    }

    internal RegistryManager Manager => _manager;

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        _manager.CloseCallbacks();
    }

    internal void EnsureOpen()
    {
        if (_closed) throw new InvalidOperationException("Registry registration scope is closed.");
    }
}
