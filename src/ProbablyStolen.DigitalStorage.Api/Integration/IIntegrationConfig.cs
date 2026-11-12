namespace ProbablyStolen.DigitalStorage.Api.Integration;

/// <summary>
/// The settings of one integration plugin, shown in Digital Storage's settings under the plugin's own header
/// (<see cref="IIntegrationPlugin.SettingSectionKey"/>). The host defines the plugin's on/off switch first; options
/// defined here follow it. Integration headers always come after every built-in Digital Storage section, one header per
/// installed integration.
/// </summary>
/// <remarks>
/// Keys are local to the plugin (lowercase letters, digits and <c>_</c>); the host stores them under the plugin's id, so
/// two plugins can use the same key. Labels and tooltips are Digital Storage localization keys. Values persist in the
/// native mod settings file and survive updates; a key no longer defined keeps its stored value.
/// </remarks>
public interface IIntegrationConfig
{
    /// <summary>Defines an on/off option.</summary>
    IIntegrationOption<bool> DefineBool(string key, bool defaultValue, string labelKey, string tooltipKey);

    /// <summary>Defines a whole-number option; stored values outside the range are clamped when read.</summary>
    IIntegrationOption<int> DefineInt(string key, int defaultValue, int min, int max, string labelKey, string tooltipKey);
}

/// <summary>The current value of one integration option.</summary>
public interface IIntegrationOption<out T>
{
    /// <summary>Gets the value the player set, or the default.</summary>
    T Value { get; }
}
