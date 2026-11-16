using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal sealed class StorageDiagnostics
{
    private const int MaximumVerboseEvents = 64;
    private const int MaximumFaultsPerEvent = 4;
    private readonly ConditionalWeakTable<object, FailureMarker> _failures = new();
    private readonly ConditionalWeakTable<object, HashSet<string>> _verbose = new();
    private readonly Action<string> _sink;
    private readonly Action<string> _verboseSink;
    private readonly Func<bool> _enabled;
    private int _verboseEvents;

    internal StorageDiagnostics(Action<string> sink, Func<bool> enabled, Action<string>? verboseSink = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _verboseSink = verboseSink ?? sink;
    }

    internal void Failure(object? instance, string eventCode, string message, Func<string>? context = null) =>
        RecordFailure(instance, eventCode, message, null, context);

    internal void Failure(object? instance, string eventCode, string message, Exception error, Func<string>? context = null) =>
        RecordFailure(instance, eventCode, message, error, context);

    private void RecordFailure(object? instance, string eventCode, string message, Exception? error, Func<string>? context)
    {
        try
        {
            var exception = error == null ? null : ExceptionText(error);
            if (instance == null)
            {
                Write(_sink, eventCode, message, exception, context);
                return;
            }

            var marker = _failures.GetValue(instance, _ => new FailureMarker());
            lock (marker)
            {
                if (exception == null)
                {
                    if (!marker.Messages.Contains(eventCode) && Write(_sink, eventCode, message, null, context))
                        marker.Messages.Add(eventCode);
                    return;
                }

                if (!marker.Exceptions.TryGetValue(eventCode, out var seen))
                    marker.Exceptions[eventCode] = seen = new HashSet<string>(StringComparer.Ordinal);
                using var hash = SHA256.Create();
                var fingerprint = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(exception)));
                if (seen.Contains(fingerprint)) return;
                if (seen.Count >= MaximumFaultsPerEvent)
                {
                    if (!marker.Limited.Contains(eventCode) &&
                        Write(_sink, eventCode, "Digital Storage: further distinct failures for this instance are suppressed.", null, context))
                        marker.Limited.Add(eventCode);
                    return;
                }

                // Failed output must remain eligible for the next report of the same fault.
                if (Write(_sink, eventCode, message, exception, context)) seen.Add(fingerprint);
            }
        }
        catch
        {
            Report(_sink, eventCode, message, error, context);
        }
    }

    internal void VerboseOnce(object instance, string eventCode, string message, Func<string>? context = null)
    {
        try
        {
            if (!_enabled()) return;
            var seen = _verbose.GetValue(instance, _ => new HashSet<string>(StringComparer.Ordinal));
            lock (seen)
            {
                if (!seen.Contains(eventCode) && WriteVerbose(eventCode, message, context)) seen.Add(eventCode);
            }
        }
        catch { }
    }

    internal void Verbose(string eventCode, string message, Func<string>? context = null)
    {
        try { if (_enabled()) WriteVerbose(eventCode, message, context); }
        catch { }
    }

    private bool WriteVerbose(string eventCode, string message, Func<string>? context)
    {
        if (Interlocked.Increment(ref _verboseEvents) > MaximumVerboseEvents)
        {
            Interlocked.Decrement(ref _verboseEvents);
            return false;
        }

        if (Write(_verboseSink, eventCode, message, null, context)) return true;
        Interlocked.Decrement(ref _verboseEvents);
        return false;
    }

    internal static void Report(Action<string> sink, string eventCode, string message, Exception? error = null,
        Func<string>? context = null)
    {
        try { Write(sink, eventCode, message, error == null ? null : ExceptionText(error), context); }
        catch { }
    }

    internal static string Context(Func<string> read)
    {
        try { return read(); }
        catch { return "context=unavailable"; }
    }

    internal static string Field(string name, Func<object?> read)
    {
        try
        {
            var value = Convert.ToString(read(), CultureInfo.InvariantCulture) ?? "none";
            if (value.Length > 128) value = value.Substring(0, 128) + "...";
            return name + "=" + value.Replace('\r', ' ').Replace('\n', ' ');
        }
        catch { return name + "=unavailable"; }
    }

    private static string ExceptionText(Exception error)
    {
        try { return error.ToString(); }
        catch
        {
            var text = new StringBuilder(error.GetType().FullName);
            try { text.Append(": ").Append(error.Message); }
            catch { }
            try { text.AppendLine().Append(error.StackTrace); }
            catch { }
            if (error.InnerException != null) text.AppendLine().Append(ExceptionText(error.InnerException));
            return text.ToString();
        }
    }

    private static bool Write(Action<string> sink, string eventCode, string message, string? exception, Func<string>? context)
    {
        try
        {
            var output = new StringBuilder(message).Append(" event=").Append(eventCode);
            if (context != null) output.Append(' ').Append(Context(context));
            if (exception != null) output.AppendLine().Append(exception);
            sink(output.ToString());
            return true;
        }
        catch { return false; }
    }

    private sealed class FailureMarker
    {
        internal readonly HashSet<string> Messages = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, HashSet<string>> Exceptions = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Limited = new(StringComparer.Ordinal);
    }
}
