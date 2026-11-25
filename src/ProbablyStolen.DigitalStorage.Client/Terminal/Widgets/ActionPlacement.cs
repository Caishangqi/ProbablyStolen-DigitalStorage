namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>Where a function button shows on its dock: directly, or inside the dock's function selection panel.</summary>
public enum ActionPlacement
{
    /// <summary>Directly on the dock.</summary>
    Rail = 0,

    /// <summary>In the panel opened by the dock's function selection button.</summary>
    Picker = 1
}
