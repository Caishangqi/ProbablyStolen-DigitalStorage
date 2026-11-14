namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>Stores the terminal view a terminal item last switched to; the platform keeps it as an item tag.</summary>
public interface ICurrentTerminalStore
{
    /// <summary>Gets the stored view name, or null when none is stored. May throw when the store cannot be read.</summary>
    string? Read();

    /// <summary>Stores a view name. May throw when the store cannot be written.</summary>
    void Write(string terminalName);
}
