using System;

namespace ProbablyStolen.DigitalStorage.Api.Registries;

/// <summary>Provides a callback phase for declaring registry identities and value types.</summary>
public sealed class NewRegistryEvent
{
    private readonly Action<Identifier, Type, Func<Identifier, object>> _create;
    private bool _active = true;

    internal NewRegistryEvent(Action<Identifier, Type, Func<Identifier, object>> create) => _create = create;

    internal void Close() => _active = false;

    /// <summary>Declares a typed registry.</summary>
    /// <typeparam name="T">The registry value type.</typeparam>
    /// <param name="id">The registry identifier.</param>
    public void Create<T>(Identifier id)
    {
        if (!_active) throw new InvalidOperationException("Registry declaration callback has ended.");
        Create(RegistryBuilder<T>.Create(id));
    }

    /// <summary>Declares a typed registry using a named builder.</summary>
    /// <typeparam name="T">The registry value type.</typeparam>
    /// <param name="builder">The registry builder.</param>
    public void Create<T>(RegistryBuilder<T> builder)
    {
        if (!_active) throw new InvalidOperationException("Registry declaration callback has ended.");
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        _create(builder.RegistryId, typeof(T), _ => builder.Build());
    }
}
