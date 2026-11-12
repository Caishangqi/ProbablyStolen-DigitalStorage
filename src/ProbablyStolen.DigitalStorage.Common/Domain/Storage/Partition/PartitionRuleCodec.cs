using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>
/// Encodes partition rules as versioned text for one cell tag. The first line is <c>v1</c>; each further line is one
/// rule: <c>id TAB slot TAB name TAB display TAB conditions</c>. Every text token is Base64 of UTF-8, so names and
/// identifiers never collide with separators; <c>-</c> stands for an absent name or display key.
/// </summary>
/// <remarks>
/// A condition is <c>field|operator|target|upper</c> with conditions separated by <c>,</c>; a target is empty, <c>t</c>
/// followed by Base64 text, or <c>n</c> followed by an invariant integer. Decoding never skips what it cannot read: any
/// malformed line rejects the whole text, so a damaged whitelist or blacklist cannot silently widen.
/// </remarks>
public static class PartitionRuleCodec
{
    private const string Version = "v1";
    private const string Absent = "-";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Encodes the rules of a configuration; the mode and revision are stored separately.</summary>
    public static string Encode(IEnumerable<PartitionRule> rules)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        var builder = new StringBuilder(Version);
        foreach (var rule in rules)
        {
            builder.Append('\n')
                .Append(Text(rule.RuleId)).Append('\t')
                .Append(rule.Slot.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(rule.Name == null ? Absent : Text(rule.Name)).Append('\t')
                .Append(rule.DisplayKey == null ? Absent : Key(rule.DisplayKey)).Append('\t')
                .Append(string.Join(",", rule.Conditions.Select(Condition)));
        }

        return builder.ToString();
    }

    /// <summary>Decodes rules, or returns false for malformed text or an unknown version.</summary>
    public static bool TryDecode(string? text, out IReadOnlyList<PartitionRule> rules)
    {
        rules = Array.Empty<PartitionRule>();
        if (text == null) return false;
        var lines = text.Split('\n');
        if (lines[0] != Version) return false;
        var result = new List<PartitionRule>(lines.Length - 1);
        for (var index = 1; index < lines.Length; index++)
        {
            var rule = DecodeLine(lines[index]);
            if (rule == null) return false;
            result.Add(rule);
        }

        rules = result.AsReadOnly();
        return true;
    }

    /// <summary>
    /// Decodes every readable rule and counts the lines that cannot be read. A damaged line costs only its own rule;
    /// text of an unknown format version yields no rules and counts each of its lines.
    /// </summary>
    public static IReadOnlyList<PartitionRule> DecodeReadable(string text, out int dropped)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        var lines = text.Split('\n');
        if (lines[0] != Version)
        {
            dropped = Math.Max(1, lines.Length - 1);
            return Array.Empty<PartitionRule>();
        }

        dropped = 0;
        var result = new List<PartitionRule>(lines.Length - 1);
        for (var index = 1; index < lines.Length; index++)
        {
            var rule = DecodeLine(lines[index]);
            if (rule == null) dropped++;
            else result.Add(rule);
        }

        return result.AsReadOnly();
    }

    private static PartitionRule? DecodeLine(string line)
    {
        try
        {
            var fields = line.Split('\t');
            if (fields.Length != 5) return null;
            var slot = int.Parse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var name = fields[2] == Absent ? null : DecodeText(fields[2]);
            var display = fields[3] == Absent ? null : DecodeKey(fields[3]);
            var conditions = fields[4].Length == 0
                ? Array.Empty<PartitionCondition>()
                : fields[4].Split(',').Select(DecodeCondition).ToArray();
            return new PartitionRule(DecodeText(fields[0]), slot, name, display, conditions);
        }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static string Condition(PartitionCondition condition) =>
        $"{Text(condition.FieldId)}|{((int)condition.Operator).ToString(CultureInfo.InvariantCulture)}|{Value(condition.Target)}|{Value(condition.Upper)}";

    private static PartitionCondition DecodeCondition(string token)
    {
        var parts = token.Split('|');
        if (parts.Length != 4) throw new FormatException("A condition has four parts.");
        var op = int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
        return new PartitionCondition(DecodeText(parts[0]), (PartitionOperator)op, DecodeValue(parts[2]), DecodeValue(parts[3]));
    }

    private static string Value(PartitionValue? value) => value == null ? string.Empty
        : value.Kind == PartitionValueKind.Text ? "t" + Text(value.Text!)
        : "n" + value.Number.ToString(CultureInfo.InvariantCulture);

    private static PartitionValue? DecodeValue(string token) => token.Length == 0 ? null
        : token[0] == 't' ? PartitionValue.Of(DecodeText(token.Substring(1)))
        : token[0] == 'n' ? PartitionValue.Of(long.Parse(token.Substring(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))
        : throw new FormatException("Unknown value kind.");

    private static string Key(ItemKey key) => key.IsCanonical
        ? "c" + Text(key.Identifier) + "." + Convert.ToBase64String(key.CopyCanonicalState())
        : "l" + Text(key.Identifier);

    private static ItemKey DecodeKey(string token)
    {
        if (token.Length < 2) throw new FormatException("A display key is empty.");
        if (token[0] == 'l') return new ItemKey(DecodeText(token.Substring(1)));
        if (token[0] != 'c') throw new FormatException("Unknown display key kind.");
        var dot = token.IndexOf('.');
        if (dot < 0) throw new FormatException("A canonical display key has two parts.");
        return ItemKey.FromCanonical(DecodeText(token.Substring(1, dot - 1)), Convert.FromBase64String(token.Substring(dot + 1)));
    }

    private static string Text(string value) => Convert.ToBase64String(Utf8.GetBytes(value));

    private static string DecodeText(string token) => Utf8.GetString(Convert.FromBase64String(token));
}
