using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProbablyStolen.DigitalStorage.Common.Diagnostics;

internal static class LogFormatter
{
    internal const int MaximumStringLength = 128;

    internal static bool IsToken(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || value[0] is < 'a' or > 'z') return false;
        var separator = false;
        foreach (var c in value)
        {
            if (c == '_' && !separator) separator = true;
            else if (c is >= 'a' and <= 'z' or >= '0' and <= '9') separator = false;
            else return false;
        }
        return !separator;
    }

    internal static string Format(LogCategory category, LogVerbosity level, string eventName, string message,
        IReadOnlyList<LogField>? fields, long? operationId, string? exceptionType)
    {
        var line = new StringBuilder().Append("category=").Append(category.Name)
            .Append(" level=").Append(level.ToString().ToLowerInvariant()).Append(" event=").Append(eventName)
            .Append(" message=").Append(Quote(message));
        if (operationId.HasValue) line.Append(" operation_id=").Append(operationId.Value.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(exceptionType)) line.Append(" exception_type=").Append(Quote(exceptionType));
        for (var i = 0; fields != null && i < Math.Min(fields.Count, 16); i++)
        {
            var field = fields[i];
            if (field.Name != null) line.Append(' ').Append(field.Name).Append('=').Append(field.FormattedValue);
        }
        return line.ToString();
    }

    internal static string Quote(string? value)
    {
        var text = new StringBuilder().Append('"');
        if (value != null)
        {
            for (var i = 0; i < Math.Min(value.Length, MaximumStringLength); i++)
            {
                var c = value[i];
                if (c is '\\' or '"') text.Append('\\').Append(c);
                else if (char.IsControl(c) || char.IsSurrogate(c) || c is '\u2028' or '\u2029')
                    text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);
            }
            if (value.Length > MaximumStringLength) text.Append("...");
        }
        return text.Append('"').ToString();
    }
}
