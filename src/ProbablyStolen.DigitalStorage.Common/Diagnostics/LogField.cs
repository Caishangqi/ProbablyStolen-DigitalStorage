using System;
using System.Globalization;

namespace ProbablyStolen.DigitalStorage.Common.Diagnostics;

internal readonly struct LogField
{
    private LogField(string name, string formattedValue)
    {
        if (!LogFormatter.IsToken(name)) throw new ArgumentException("Invalid log field name.", nameof(name));
        Name = name;
        FormattedValue = formattedValue;
    }

    internal string Name { get; }
    internal string FormattedValue { get; }

    internal static LogField Of(string name, string? value) => new(name, LogFormatter.Quote(value));
    internal static LogField Of(string name, bool value) => new(name, value ? "true" : "false");
    internal static LogField Of(string name, long value) => new(name, value.ToString(CultureInfo.InvariantCulture));
    internal static LogField Of(string name, ulong value) => new(name, value.ToString(CultureInfo.InvariantCulture));
    internal static LogField Of(string name, int value) => Of(name, (long)value);
    internal static LogField Of(string name, uint value) => Of(name, (ulong)value);
}
