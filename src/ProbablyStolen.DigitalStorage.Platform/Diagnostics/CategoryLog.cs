using System;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

/// <summary>
/// Writes free-text records to one log category, so players can turn them off and choose their level in the mod
/// settings. The text follows the category, level and event and is not shortened; callers keep it to identifiers,
/// counts and exception types unless a setting asks for more.
/// </summary>
internal sealed class CategoryLog
{
    private readonly DiagnosticLogger _logger;
    private readonly LogCategory _category;

    internal CategoryLog(DiagnosticLogger logger, LogCategory category)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _category = category ?? throw new ArgumentNullException(nameof(category));
    }

    internal bool IsEnabled(LogVerbosity level) => _logger.IsEnabled(_category, level);

    internal void Error(string eventName, string text) => Write(LogVerbosity.Error, eventName, text);

    internal void Warning(string eventName, string text) => Write(LogVerbosity.Warning, eventName, text);

    internal void Info(string eventName, string text) => Write(LogVerbosity.Info, eventName, text);

    internal void Verbose(string eventName, string text) => Write(LogVerbosity.Verbose, eventName, text);

    internal void Write(LogVerbosity level, string eventName, string text) =>
        _logger.WriteText(_category, level, eventName, text);

    /// <summary>A writer for components that report through a plain text callback.</summary>
    internal Action<string> Sink(LogVerbosity level, string eventName) => text => Write(level, eventName, text);
}
