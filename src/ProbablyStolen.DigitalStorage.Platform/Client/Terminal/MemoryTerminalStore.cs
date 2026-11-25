using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Keeps a window's chosen view in memory, for machine windows that store nothing on their item.</summary>
internal sealed class MemoryTerminalStore : ICurrentTerminalStore
{
    private string? _name;

    public string? Read() => _name;

    public void Write(string terminalName) => _name = terminalName;
}
