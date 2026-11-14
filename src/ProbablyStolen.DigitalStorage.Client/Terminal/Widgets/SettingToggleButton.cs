using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>Cycles an enum setting on each press, backwards when the press asks for it.</summary>
/// <typeparam name="T">The setting value type.</typeparam>
public sealed class SettingToggleButton<T> : TerminalButton where T : struct, Enum
{
    private readonly IConfigManager _config;

    /// <summary>Creates a toggle for a setting.</summary>
    public SettingToggleButton(Setting<T> setting, IConfigManager config) : base(setting?.Name ?? string.Empty)
    {
        Setting = setting ?? throw new ArgumentNullException(nameof(setting));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>Gets the setting this button changes.</summary>
    public Setting<T> Setting { get; }

    /// <summary>Gets the stored value.</summary>
    public T GetCurrentValue() => _config.GetSetting(Setting);

    /// <summary>Gets the value after the current one, or before it when <paramref name="backwards"/>.</summary>
    /// <remarks>A stored value outside the setting's values starts the cycle from its first value.</remarks>
    public T GetNextValue(bool backwards)
    {
        var values = Setting.Values;
        var index = IndexOf(values, GetCurrentValue());
        if (index < 0) return values[0];
        var step = backwards ? values.Count - 1 : 1;
        return values[(index + step) % values.Count];
    }

    /// <summary>Stores a value.</summary>
    public void Set(T value) => _config.PutSetting(Setting, value);

    /// <inheritdoc />
    public override ButtonAppearance GetAppearance(TerminalContext context) => Setting.GetAppearance(GetCurrentValue());

    /// <inheritdoc />
    public override void OnPress(TerminalContext context, bool backwards)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        Set(GetNextValue(backwards));
        context.Commands.NotifySettingChanged(Setting.Name);
    }

    private static int IndexOf(IReadOnlyList<T> values, T value)
    {
        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < values.Count; index++)
            if (comparer.Equals(values[index], value)) return index;
        return -1;
    }
}
