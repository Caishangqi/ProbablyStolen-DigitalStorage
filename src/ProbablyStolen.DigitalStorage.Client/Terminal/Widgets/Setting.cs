using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// A named enum setting with its values in cycling order and the appearance of each value. The appearance lives with
/// the feature that defines the setting, so a new setting needs no change to the shared button.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class Setting<T> where T : struct, Enum
{
    private readonly Func<T, ButtonAppearance> _appearance;

    /// <summary>Creates a setting.</summary>
    /// <param name="name">The unique setting name, also the button id.</param>
    /// <param name="values">The values in cycling order; at least one, without repeats.</param>
    /// <param name="appearance">Maps a value to its button appearance.</param>
    public Setting(string name, IReadOnlyList<T> values, Func<T, ButtonAppearance> appearance)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("A setting name is required.", nameof(name));
        if (values == null || values.Count == 0) throw new ArgumentException("A setting needs at least one value.", nameof(values));
        if (values.Distinct().Count() != values.Count) throw new ArgumentException("Setting values must not repeat.", nameof(values));
        Name = name;
        Values = values.ToArray();
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
    }

    /// <summary>Gets the setting name.</summary>
    public string Name { get; }

    /// <summary>Gets the values in cycling order.</summary>
    public IReadOnlyList<T> Values { get; }

    /// <summary>Gets the appearance of a value.</summary>
    public ButtonAppearance GetAppearance(T value) => _appearance(value);
}
