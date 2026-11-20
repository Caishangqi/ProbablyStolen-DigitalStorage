using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal sealed class CellDiagnostics
{
    private const int MaximumVerboseEvents = 64;
    private const int MaximumFaultsPerEvent = 4;
    private readonly DiagnosticLogger _log;
    private readonly LogCategory _category;
    private readonly ConditionalWeakTable<object, SeenEvents> _instances = new();
    private int _verboseEvents;

    internal CellDiagnostics(DiagnosticLogger log, LogCategory category) => (_log, _category) = (log, category);

    internal bool IsEnabled(LogVerbosity level) => _log.IsEnabled(_category, level);

    internal void Write(LogVerbosity level, string eventName, string message, Func<IReadOnlyList<LogField>>? fields = null,
        Exception? error = null) => _log.Write(_category, level, eventName, message, fields, exceptionType: error?.GetType().Name);

    internal void Once(object instance, LogVerbosity level, string eventName, string message,
        Func<IReadOnlyList<LogField>> fields, Exception? error = null)
    {
        if (!_log.IsEnabled(_category, level)) return;
        try
        {
            var seen = _instances.GetOrCreateValue(instance);
            if (level == LogVerbosity.Verbose && _verboseEvents >= MaximumVerboseEvents) return;
            if (!seen.Events.TryGetValue((level, eventName), out var faults))
                seen.Events[(level, eventName)] = faults = new HashSet<string>(StringComparer.Ordinal);
            var fingerprint = string.Empty;
            if (error != null)
            {
                using var hash = SHA256.Create();
                fingerprint = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(error.ToString())));
            }
            if (faults.Contains(fingerprint)) return;
            if (faults.Count >= MaximumFaultsPerEvent)
            {
                if (!seen.Limited.Contains(eventName) && _log.Write(_category, level, eventName,
                    "Further distinct failures for this cell are limited.", fields)) seen.Limited.Add(eventName);
                return;
            }
            if (!_log.Write(_category, level, eventName, message, fields, exceptionType: error?.GetType().Name)) return;
            faults.Add(fingerprint);
            if (level == LogVerbosity.Verbose) ++_verboseEvents;
        }
        catch { }
    }

    private sealed class SeenEvents
    {
        internal readonly Dictionary<(LogVerbosity, string), HashSet<string>> Events = new();
        internal readonly HashSet<string> Limited = new(StringComparer.Ordinal);
    }
}
