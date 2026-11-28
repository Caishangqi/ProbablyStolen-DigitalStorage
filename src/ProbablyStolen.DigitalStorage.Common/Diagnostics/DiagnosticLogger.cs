using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Common.Diagnostics;

internal readonly struct LogCategorySettings
{
    internal LogCategorySettings(bool enabled, LogVerbosity verbosity) => (Enabled, Verbosity) = (enabled, verbosity);

    internal bool Enabled { get; }
    internal LogVerbosity Verbosity { get; }
}

internal sealed class DiagnosticLogger
{
    private const int MaximumTemporaryWrites = 64;
    private readonly LogCategories _categories;
    private readonly Func<LogCategory, LogCategorySettings> _settings;
    private readonly Action<LogVerbosity, string> _sink;
    private readonly object _tempLock = new();
    private int _temporaryWrites;
    private bool _temporarySuppressed;

    internal DiagnosticLogger(LogCategories categories, Func<LogCategory, LogCategorySettings> settings,
        Action<LogVerbosity, string> sink)
    {
        _categories = categories;
        _settings = settings;
        _sink = sink;
    }

    internal static bool IsLevel(LogVerbosity value) => value is >= LogVerbosity.Error and <= LogVerbosity.Verbose;

    internal bool IsEnabled(LogCategory category, LogVerbosity level)
    {
        try
        {
            if (!IsLevel(level) || !_categories.Contains(category)) return false;
            var settings = _settings(category);
            return settings.Enabled && level <= (IsLevel(settings.Verbosity) ? settings.Verbosity : category.DefaultVerbosity);
        }
        catch { return false; }
    }

    internal bool Write(LogCategory category, LogVerbosity level, string eventName, string message,
        Func<IReadOnlyList<LogField>>? fields = null, long? operationId = null, string? exceptionType = null) =>
        Emit(category, level, eventName,
            () => LogFormatter.Format(category, level, eventName, message, fields?.Invoke(), operationId, exceptionType));

    /// <summary>
    /// Writes free text after the category, level and event, without the length limit of a message. For multi-line
    /// detail such as diagnostic traces and item dumps; the caller decides what the text may contain.
    /// </summary>
    internal bool WriteText(LogCategory category, LogVerbosity level, string eventName, string text) =>
        Emit(category, level, eventName, () => LogFormatter.FormatText(category, level, eventName, text));

    private bool Emit(LogCategory category, LogVerbosity level, string eventName, Func<string> format)
    {
        if (!IsEnabled(category, level) || !LogFormatter.IsToken(eventName)) return false;
        if (category.Id != "log_temp") return Sink(level, format);
        lock (_tempLock)
        {
            if (_temporaryWrites < MaximumTemporaryWrites)
            {
                ++_temporaryWrites;
                return Sink(level, format);
            }
            if (!_temporarySuppressed)
            {
                _temporarySuppressed = true;
                Sink(level, () => LogFormatter.Format(category, level, "temporary_log_suppressed",
                    "Temporary log limit reached.", null, null, null));
            }
            return false;
        }
    }

    private bool Sink(LogVerbosity level, Func<string> format)
    {
        try
        {
            _sink(level, format());
            return true;
        }
        catch { return false; }
    }
}
