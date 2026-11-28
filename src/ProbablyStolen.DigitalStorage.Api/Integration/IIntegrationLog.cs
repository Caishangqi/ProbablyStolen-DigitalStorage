namespace ProbablyStolen.DigitalStorage.Api.Integration;

/// <summary>
/// Writes a plugin's records to the Digital Storage compatibility log, which players can turn off and filter by level
/// in the mod settings. Each record carries the plugin id. Keep the text to identifiers, counts and exception types.
/// </summary>
public interface IIntegrationLog
{
    /// <summary>Writes a problem the plugin recovered from.</summary>
    /// <param name="eventName">A lowercase snake_case event name; other names are not written.</param>
    /// <param name="text">The record text.</param>
    void Warning(string eventName, string text);

    /// <summary>Writes an ordinary outcome worth reporting.</summary>
    /// <param name="eventName">A lowercase snake_case event name; other names are not written.</param>
    /// <param name="text">The record text.</param>
    void Info(string eventName, string text);
}
