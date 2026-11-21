using System;
using System.Collections.Generic;
using System.Text;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>The outcome of reading a terminal's stacking tag.</summary>
public enum StackingConfigStatus
{
    /// <summary>The text was read.</summary>
    Ok,

    /// <summary>The terminal has no tag; the default configuration applies.</summary>
    Missing,

    /// <summary>Valid settings from a retired version. The terminal starts with defaults.</summary>
    Reset,

    /// <summary>The text cannot be read; it is kept as it is and grouping stays off.</summary>
    Unreadable
}

/// <summary>A read configuration, or why it could not be read.</summary>
public sealed class StackingConfigReadResult
{
    private StackingConfigReadResult(StackingConfigStatus status, StackingConfig? config, string? raw, string? reason)
    {
        Status = status;
        Config = config;
        Raw = raw;
        Reason = reason;
    }

    /// <summary>Gets the status.</summary>
    public StackingConfigStatus Status { get; }

    /// <summary>Gets the current configuration or defaults for missing or retired settings. Null when unreadable.</summary>
    public StackingConfig? Config { get; }

    /// <summary>Gets the unreadable text, kept so it is never overwritten by accident.</summary>
    public string? Raw { get; }

    /// <summary>Gets a short, data-free reason for diagnostics when unreadable.</summary>
    public string? Reason { get; }

    internal static StackingConfigReadResult Ok(StackingConfig config) => new(StackingConfigStatus.Ok, config, null, null);

    internal static StackingConfigReadResult Missing() => new(StackingConfigStatus.Missing, StackingConfig.Default, null, null);

    internal static StackingConfigReadResult Reset() => new(StackingConfigStatus.Reset, StackingConfig.Default, null, null);

    internal static StackingConfigReadResult Unreadable(string raw, string reason) =>
        new(StackingConfigStatus.Unreadable, null, raw, reason);
}

/// <summary>
/// Encodes v3 terminal settings as a switch, marker color and Base64-encoded rule rows.
/// Valid v1 and v2 settings start from defaults because v3 retires the built-in tag mappings.
/// Malformed or unknown settings remain unreadable.
/// </summary>
public static class StackingConfigCodec
{
    /// <summary>The longest text accepted.</summary>
    public const int MaxLength = 16384;

    private const string Version = "v3";
    private const string MarkerPrefix = "marker\t";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Encodes a configuration.</summary>
    public static string Encode(StackingConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        var builder = new StringBuilder();
        builder.Append(Version).Append('\n').Append("enabled\t").Append(config.Enabled ? '1' : '0')
            .Append('\n').Append(MarkerPrefix).Append(config.Marker.Format());
        foreach (var rule in config.Rules)
        {
            builder.Append('\n').Append("rule\t").Append(Convert.ToBase64String(Utf8.GetBytes(rule.FieldId)))
                .Append('\t').Append(rule.Mode == StackingMode.Ignore ? "ignore" : "match");
        }

        var text = builder.ToString();
        if (text.Length > MaxLength) throw new ArgumentException("The configuration is too long to save.", nameof(config));
        return text;
    }

    /// <summary>Decodes a tag value; null or empty text means the terminal has no configuration.</summary>
    public static StackingConfigReadResult Decode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return StackingConfigReadResult.Missing();
        if (text!.Length > MaxLength) return StackingConfigReadResult.Unreadable(text, "too_long");
        var lines = text.Split('\n');
        var hasMarker = lines[0] != "v1";
        var reset = lines[0] is "v1" or "v2";
        if (!reset && lines[0] != Version) return StackingConfigReadResult.Unreadable(text, "unknown_version");
        if (lines.Length < 2) return StackingConfigReadResult.Unreadable(text, "missing_switch");
        bool enabled;
        switch (lines[1])
        {
            case "enabled\t1": enabled = true; break;
            case "enabled\t0": enabled = false; break;
            default: return StackingConfigReadResult.Unreadable(text, "bad_switch");
        }

        var marker = StackingMarker.Default;
        var first = 2;
        if (hasMarker)
        {
            if (lines.Length < 3 || !lines[2].StartsWith(MarkerPrefix, StringComparison.Ordinal) ||
                !TryReadMarker(lines[2].Substring(MarkerPrefix.Length), out marker))
                return StackingConfigReadResult.Unreadable(text, "bad_marker");
            first = 3;
        }

        if (lines.Length - first > StackingConfig.MaxRules) return StackingConfigReadResult.Unreadable(text, "too_many_rules");
        var rules = new List<StackingRule>();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (var i = first; i < lines.Length; i++)
        {
            var parts = lines[i].Split('\t');
            if (parts.Length != 3 || parts[0] != "rule") return StackingConfigReadResult.Unreadable(text, "bad_line");
            var fieldId = DecodeField(parts[1]);
            if (fieldId == null || !StackingRule.IsValidFieldId(fieldId))
                return StackingConfigReadResult.Unreadable(text, "bad_field");
            StackingMode mode;
            switch (parts[2])
            {
                case "match": mode = StackingMode.Match; break;
                case "ignore": mode = StackingMode.Ignore; break;
                default: return StackingConfigReadResult.Unreadable(text, "bad_mode");
            }

            if (!fields.Add(fieldId)) return StackingConfigReadResult.Unreadable(text, "duplicate_field");
            rules.Add(new StackingRule(fieldId, mode));
        }

        return reset ? StackingConfigReadResult.Reset() : StackingConfigReadResult.Ok(new StackingConfig(enabled, rules, marker));
    }

    /// <summary>Saved markers are always the upper case six-digit form; anything else is damage.</summary>
    private static bool TryReadMarker(string token, out StackingMarker marker) =>
        StackingMarker.TryParse(token, out marker) && marker.Format() == token;

    private static string? DecodeField(string token)
    {
        try
        {
            return Utf8.GetString(Convert.FromBase64String(token));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
