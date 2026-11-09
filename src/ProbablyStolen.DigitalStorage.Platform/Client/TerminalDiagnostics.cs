using System;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Bounds diagnostic output and keeps logger failures outside item operations.</summary>
internal sealed class TerminalDiagnostics
{
    private const int Limit = 120;
    private readonly Func<bool> _enabled;
    private readonly Action<string> _write;
    private int _count;

    internal TerminalDiagnostics(Func<bool> enabled, Action<string> write)
    {
        _enabled = enabled;
        _write = write;
    }

    // Callers supply only fixed event codes, UI metadata, flags and aggregate counts.
    internal void Record(string eventCode, string details)
    {
        try
        {
            if (!_enabled() || _count > Limit)
            {
                return;
            }

            _count++;
            _write(_count > Limit
                ? "[DS.Terminal] limit reached; reopen the terminal to start a new diagnostic session."
                : $"[DS.Terminal] #{_count} {eventCode} {details}");
        }
        catch
        {
            // Diagnostic failures must not change whether an inventory commit runs.
        }
    }
}
