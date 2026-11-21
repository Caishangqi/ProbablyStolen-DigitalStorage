using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// Reads and writes one terminal's stacking settings. A write counts only when reading the tag back yields the same
/// settings, so a failed or refused write never changes what is in effect.
/// </summary>
public sealed class StackingConfigStore
{
    private readonly ITerminalFeatureData _data;
    private string? _cachedRaw;
    private StackingConfigReadResult? _cached;

    /// <summary>Creates a store over a terminal's data.</summary>
    public StackingConfigStore(ITerminalFeatureData data) => _data = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Reads the settings; the same text is decoded once.</summary>
    public StackingConfigReadResult Read()
    {
        var raw = _data.Read(StackingModule.ConfigTag);
        if (_cached != null && raw == _cachedRaw) return _cached;
        _cachedRaw = raw;
        _cached = StackingConfigCodec.Decode(raw);
        return _cached;
    }

    /// <summary>Gets the settings in effect, including defaults for missing or retired settings.</summary>
    public StackingConfig? Effective() => Read().Config;

    /// <summary>Writes a candidate; false leaves the previous settings in effect.</summary>
    public bool TryCommit(StackingConfig candidate)
    {
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));
        if (Read().Status == StackingConfigStatus.Unreadable) return false;
        string text;
        try
        {
            text = StackingConfigCodec.Encode(candidate);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!_data.TryWrite(StackingModule.ConfigTag, text)) return false;
        var read = Read();
        return read.Status == StackingConfigStatus.Ok && candidate.Equals(read.Config);
    }
}
