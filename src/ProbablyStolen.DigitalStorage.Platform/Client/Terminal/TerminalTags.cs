using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>
/// The view a terminal item last switched to. The tag is part of the native item state, so the save graph keeps it
/// with the terminal; a terminal without the tag,
/// including every terminal from an older save, opens the storage view. Only the view name is stored.
/// </summary>
internal sealed class TerminalTags : ICurrentTerminalStore
{
    internal const string CurrentTerminalTag = "digital_storage.current_terminal";

    private readonly GameItem _terminal;

    internal TerminalTags(GameItem terminal) => _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));

    /// <inheritdoc />
    public string? Read()
    {
        if (!_terminal.IsTag(CurrentTerminalTag)) return null;
        var name = _terminal.GetTagReadonly(CurrentTerminalTag).GetString();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <inheritdoc />
    public void Write(string terminalName)
    {
        if (string.IsNullOrEmpty(terminalName)) throw new ArgumentException("A view name is required.", nameof(terminalName));
        _terminal.ModifyTag(CurrentTerminalTag, state => state.Enable().SetString(terminalName));
    }
}
