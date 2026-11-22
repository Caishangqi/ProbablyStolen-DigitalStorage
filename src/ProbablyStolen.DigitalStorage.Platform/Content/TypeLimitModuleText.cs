using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal static class TypeLimitModuleText
{
    internal static string Effect(ModText text, TypeLimitRoll roll, long add, Action<Exception> reportError)
    {
        try
        {
            var template = Line(text.Get("module_type_limit_effect"), "module_type_limit_effect");
            // The shipped templates use three plain placeholders. Reject stray or escaped braces so a
            // placeholder written as literal text cannot stand in for a required value.
            var literal = template.Replace("{0}", string.Empty).Replace("{1}", string.Empty).Replace("{2}", string.Empty);
            if (literal.Contains('{') || literal.Contains('}') || !template.Contains("{0}", StringComparison.Ordinal) ||
                !template.Contains("{1}", StringComparison.Ordinal) || !template.Contains("{2}", StringComparison.Ordinal))
                throw new FormatException("The module effect must show quality and both bonuses.");
            var qualityKey = "module_quality_" + ModuleTags.QualityText(roll.Quality);
            return Line(string.Format(template, Line(text.Get(qualityKey), qualityKey), add, roll.Percent), template);
        }
        catch (Exception error)
        {
            Report(reportError, error);
            return FormattableString.Invariant($"{roll.Quality}: type limit +{add} and +{roll.Percent}%");
        }
    }

    internal static string Invalid(ModText text, Action<Exception> reportError)
    {
        const string key = "module_type_limit_invalid";
        try { return Line(text.Get(key), key); }
        catch (Exception error)
        {
            Report(reportError, error);
            return "Unreadable module: no effect";
        }
    }

    private static string Line(string line, string key)
    {
        if (string.IsNullOrWhiteSpace(line) || line == key || line.Contains('\r') || line.Contains('\n'))
            throw new InvalidOperationException("Module effect localization is unavailable.");
        return line;
    }

    private static void Report(Action<Exception> reportError, Exception error)
    {
        try { reportError(error); }
        catch (Exception) { }
    }
}
