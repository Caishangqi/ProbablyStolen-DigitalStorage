using System;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal sealed class LogSetting
{
    internal const string Section = "config_section_logs";
    internal const string EnabledLabel = "log_category_enabled";

    internal LogSetting(LogCategory category) => Category = category;

    internal LogCategory Category { get; }
    internal string VerbosityKey => Category.Id + "_verbosity";
    internal string EnabledKey => Category.Id + "_enabled";

    internal LogVerbosity Normalize(LogVerbosity value) =>
        DiagnosticLogger.IsLevel(value) ? value : Category.DefaultVerbosity;

    internal int DisplayedIndex(LogVerbosity value, int index, string[] choices) =>
        DiagnosticLogger.IsLevel(value) ? index : Array.IndexOf(choices, Category.DefaultVerbosity.ToString());

    internal string Label(Func<string, string?> translate) => Localized(translate, Category.LabelKey, Category.FallbackLabel);

    internal static string Localized(Func<string, string?> translate, string? key, string fallback)
    {
        if (key == null) return fallback;
        try
        {
            var value = translate(key);
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal) ? fallback : value!;
        }
        catch { return fallback; }
    }
}
