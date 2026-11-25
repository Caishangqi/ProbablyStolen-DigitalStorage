using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>A registered dock: the side it hangs from and its title.</summary>
public sealed class DockDefinition
{
    /// <summary>Creates a dock definition.</summary>
    public DockDefinition(DockSide side, string titleKey)
    {
        if (side != DockSide.Left && side != DockSide.Right) throw new ArgumentOutOfRangeException(nameof(side));
        Side = side;
        TitleKey = string.IsNullOrWhiteSpace(titleKey)
            ? throw new ArgumentException("A title key is required.", nameof(titleKey)) : titleKey;
    }

    /// <summary>Gets the side.</summary>
    public DockSide Side { get; }

    /// <summary>Gets the localization key of the title shown above the buttons.</summary>
    public string TitleKey { get; }
}
