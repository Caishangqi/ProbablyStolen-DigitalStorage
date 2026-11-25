namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>A sub-screen pushed over a terminal view; the platform page implements it.</summary>
public interface ITerminalScreen
{
    /// <summary>Gets the id that <see cref="ActionScope.Screen"/> buttons name.</summary>
    string ScreenId { get; }
}
