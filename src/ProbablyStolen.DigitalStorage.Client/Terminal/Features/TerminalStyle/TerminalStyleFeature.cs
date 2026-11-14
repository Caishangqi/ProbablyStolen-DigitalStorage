using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.TerminalStyle;

/// <summary>
/// The terminal size toggle, on the rail for every view, since the size applies to the whole window.
/// </summary>
public static class TerminalStyleFeature
{
    /// <summary>The setting name, which is also its existing global config key.</summary>
    public const string SettingName = "terminal_view_size";

    /// <summary>The terminal size setting.</summary>
    public static readonly Setting<TerminalSize> TerminalStyle = new(SettingName,
        new[] { TerminalSize.Small, TerminalSize.Medium, TerminalSize.Tall, TerminalSize.Full },
        size =>
        {
            var name = size.ToString().ToLowerInvariant();
            return new ButtonAppearance("terminal_button_size_" + name, "terminal_button_size_tip", "terminal_size_" + name);
        });

    /// <summary>Registers the toggle after the modules button.</summary>
    public static void Register(TerminalRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.AddSetting(TerminalStyle, 10, ActionPlacement.Rail, null);
    }
}
