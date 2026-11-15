using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>
/// Named string tags on a terminal item. Tags are part of the native item state, so the save graph keeps them with the
/// terminal. A write is refused once the window no longer belongs to the loaded game, so a late callback from an old
/// window or an earlier session never writes into another terminal.
/// </summary>
internal sealed class TerminalFeatureData : ITerminalFeatureData
{
    private readonly GameItem _terminal;
    private readonly Func<bool> _isCurrent;

    internal TerminalFeatureData(GameItem terminal, Func<bool> isCurrent)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
    }

    /// <inheritdoc />
    public string? Read(string key)
    {
        if (string.IsNullOrEmpty(key) || !_terminal.IsTag(key)) return null;
        var value = _terminal.GetTagReadonly(key).GetString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <inheritdoc />
    public bool TryWrite(string key, string value)
    {
        if (string.IsNullOrEmpty(key) || value == null || !_isCurrent()) return false;
        if (_terminal is GameItemElement element && element.IsDestroyed()) return false;
        _terminal.ModifyTag(key, state => state.Enable().SetString(value));
        return true;
    }
}
