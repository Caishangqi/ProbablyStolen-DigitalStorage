using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>Reads and writes setting values.</summary>
public interface IConfigManager
{
    /// <summary>Gets the current value of a setting.</summary>
    T GetSetting<T>(Setting<T> setting) where T : struct, Enum;

    /// <summary>Stores a new value for a setting.</summary>
    void PutSetting<T>(Setting<T> setting, T value) where T : struct, Enum;
}
