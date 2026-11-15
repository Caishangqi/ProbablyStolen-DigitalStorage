using System;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Provides a callback phase for declaring data-driven registries and decoders.</summary>
public sealed class DataPackRegistryEvent
{
    private readonly Action<Identifier, Type, Func<string, object>> _register;
    private bool _active = true;

    internal DataPackRegistryEvent(Action<Identifier, Type, Func<string, object>> register) => _register = register;

    internal void Close() => _active = false;

    /// <summary>Declares a data registry and its decoder.</summary>
    /// <typeparam name="T">The registry value type.</typeparam>
    /// <param name="id">The registry identifier.</param>
    /// <param name="decoder">The decoder invoked for data values.</param>
    public void NewRegistry<T>(Identifier id, Func<string, T> decoder)
    {
        if (!_active) throw new InvalidOperationException("Data registry callback has ended.");
        if (decoder == null)
            throw new ArgumentNullException(nameof(decoder));
        _register(id, typeof(T), json => decoder(json)!);
    }
}
