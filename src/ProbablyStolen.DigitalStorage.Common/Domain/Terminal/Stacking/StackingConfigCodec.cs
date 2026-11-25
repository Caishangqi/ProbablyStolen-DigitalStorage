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

    /// <summary>
    /// Valid settings from a retired version. The terminal starts without item rules; a v3 marker color is kept. Nothing is
    /// written until the next successful edit.
    /// </summary>
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

    internal static StackingConfigReadResult Reset(StackingMarker marker) =>
        new(StackingConfigStatus.Reset, StackingConfig.Default.WithMarker(marker), null, null);

    internal static StackingConfigReadResult Unreadable(string raw, string reason) =>
        new(StackingConfigStatus.Unreadable, null, raw, reason);
}

/// <summary>Validates and encodes the stacking settings stored on a terminal item.</summary>
public static class StackingConfigCodec
{
    /// <summary>The longest text accepted.</summary>
    public const int MaxLength = 16384;

    private const string Version = "v4";
    private const string MarkerPrefix = "marker\t";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Encodes a configuration.</summary>
    public static string Encode(StackingConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        var builder = new StringBuilder();
        builder.Append(Version).Append('\n').Append(MarkerPrefix).Append(config.Marker.Format())
            .Append('\n').Append("advanced\t").Append(config.Advanced ? '1' : '0');
        foreach (var item in config.Items)
        {
            builder.Append('\n').Append("item\t").Append(EncodeText(item.ItemId)).Append('\t').Append(item.Enabled ? '1' : '0');
            foreach (var rule in item.Rules)
            {
                builder.Append('\n').Append("rule\t").Append(EncodeText(rule.FieldId))
                    .Append('\t').Append(rule.Mode == StackingMode.Ignore ? "ignore" : "match");
            }
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
        return lines[0] switch
        {
            Version => DecodeV4(text, lines),
            "v3" => DecodeRetired(text, lines, hasMarker: true),
            "v2" => DecodeRetired(text, lines, hasMarker: true),
            "v1" => DecodeRetired(text, lines, hasMarker: false),
            _ => StackingConfigReadResult.Unreadable(text, "unknown_version")
        };
    }

    private static StackingConfigReadResult DecodeV4(string text, string[] lines)
    {
        if (lines.Length < 2 || !lines[1].StartsWith(MarkerPrefix, StringComparison.Ordinal) ||
            !TryReadMarker(lines[1].Substring(MarkerPrefix.Length), out var marker))
            return StackingConfigReadResult.Unreadable(text, "bad_marker");
        bool advanced;
        switch (lines.Length < 3 ? null : lines[2])
        {
            case "advanced\t1": advanced = true; break;
            case "advanced\t0": advanced = false; break;
            default: return StackingConfigReadResult.Unreadable(text, "bad_advanced");
        }

        var items = new List<StackingItemRule>();
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        string? itemId = null;
        var enabled = false;
        var rules = new List<StackingRule>();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 3; i < lines.Length; i++)
        {
            var parts = lines[i].Split('\t');
            if (parts.Length != 3) return StackingConfigReadResult.Unreadable(text, "bad_line");
            if (parts[0] == "item")
            {
                if (itemId != null) items.Add(new StackingItemRule(itemId, enabled, rules));
                itemId = DecodeText(parts[1]);
                if (itemId == null || !StackingItemRule.IsValidItemId(itemId))
                    return StackingConfigReadResult.Unreadable(text, "bad_item");
                if (!itemIds.Add(itemId)) return StackingConfigReadResult.Unreadable(text, "duplicate_item");
                if (items.Count >= StackingConfig.MaxItems) return StackingConfigReadResult.Unreadable(text, "too_many_items");
                switch (parts[2])
                {
                    case "1": enabled = true; break;
                    case "0": enabled = false; break;
                    default: return StackingConfigReadResult.Unreadable(text, "bad_switch");
                }

                rules = new List<StackingRule>();
                fields.Clear();
                continue;
            }

            if (parts[0] != "rule") return StackingConfigReadResult.Unreadable(text, "bad_line");
            if (itemId == null) return StackingConfigReadResult.Unreadable(text, "rule_without_item");
            if (!TryReadRule(parts, out var rule, out var reason)) return StackingConfigReadResult.Unreadable(text, reason);
            if (!fields.Add(rule.FieldId)) return StackingConfigReadResult.Unreadable(text, "duplicate_field");
            if (rules.Count >= StackingItemRule.MaxRules) return StackingConfigReadResult.Unreadable(text, "too_many_rules");
            rules.Add(rule);
        }

        if (itemId != null) items.Add(new StackingItemRule(itemId, enabled, rules));
        return StackingConfigReadResult.Ok(new StackingConfig(items, marker, advanced));
    }

    /// <summary>Validates a retired version completely, so damage is still reported as unreadable.</summary>
    private static StackingConfigReadResult DecodeRetired(string text, string[] lines, bool hasMarker)
    {
        if (lines.Length < 2 || lines[1] != "enabled\t1" && lines[1] != "enabled\t0")
            return StackingConfigReadResult.Unreadable(text, "bad_switch");
        var marker = StackingMarker.Default;
        var first = 2;
        if (hasMarker)
        {
            if (lines.Length < 3 || !lines[2].StartsWith(MarkerPrefix, StringComparison.Ordinal) ||
                !TryReadMarker(lines[2].Substring(MarkerPrefix.Length), out marker))
                return StackingConfigReadResult.Unreadable(text, "bad_marker");
            first = 3;
        }

        if (lines.Length - first > StackingItemRule.MaxRules) return StackingConfigReadResult.Unreadable(text, "too_many_rules");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (var i = first; i < lines.Length; i++)
        {
            var parts = lines[i].Split('\t');
            if (parts.Length != 3 || parts[0] != "rule") return StackingConfigReadResult.Unreadable(text, "bad_line");
            if (!TryReadRule(parts, out var rule, out var reason)) return StackingConfigReadResult.Unreadable(text, reason);
            if (!fields.Add(rule.FieldId)) return StackingConfigReadResult.Unreadable(text, "duplicate_field");
        }

        return StackingConfigReadResult.Reset(lines[0] == "v3" ? marker : StackingMarker.Default);
    }

    private static bool TryReadRule(string[] parts, out StackingRule rule, out string reason)
    {
        rule = null!;
        var fieldId = DecodeText(parts[1]);
        if (fieldId == null || !StackingRule.IsValidFieldId(fieldId))
        {
            reason = "bad_field";
            return false;
        }

        switch (parts[2])
        {
            case "match": rule = new StackingRule(fieldId, StackingMode.Match); break;
            case "ignore": rule = new StackingRule(fieldId, StackingMode.Ignore); break;
            default:
                reason = "bad_mode";
                return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Saved markers are always the upper case six-digit form; anything else is damage.</summary>
    private static bool TryReadMarker(string token, out StackingMarker marker) =>
        StackingMarker.TryParse(token, out marker) && marker.Format() == token;

    private static string EncodeText(string value) => Convert.ToBase64String(Utf8.GetBytes(value));

    private static string? DecodeText(string token)
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
