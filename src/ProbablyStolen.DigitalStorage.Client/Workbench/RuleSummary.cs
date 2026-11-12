using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>
/// Describes what a rule matches in plain language, such as "Item is a vial of nightmare dust, and heat is below 40%".
/// Texts come from localization keys <c>partition_summary_&lt;operator&gt;</c> with {0} the field, {1} the value
/// and {2} the upper bound; conditions are joined with <c>partition_summary_and</c>.
/// </summary>
public static class RuleSummary
{
    /// <summary>Builds the summary.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="text">Formats a localization key with arguments.</param>
    /// <param name="valueText">Gives the display text of a target, such as an item's name or a level's label.</param>
    public static string Describe(PartitionRule rule, Func<string, object[], string> text,
        Func<PartitionField, PartitionValue, string> valueText)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (valueText == null) throw new ArgumentNullException(nameof(valueText));
        var parts = rule.Conditions.Select(condition =>
        {
            var field = PartitionFieldRegistry.Find(condition.FieldId);
            if (field == null) return text("partition_summary_unknown", new object[] { condition.FieldId });
            var name = field.Id.StartsWith(PartitionFieldRegistry.FeaturePrefix, StringComparison.Ordinal)
                ? text(field.NameKey, new object[] { field.Id.Substring(PartitionFieldRegistry.FeaturePrefix.Length) })
                : text(field.NameKey, Array.Empty<object>());
            var target = condition.Target == null ? string.Empty : valueText(field, condition.Target);
            var upper = condition.Upper == null ? string.Empty : valueText(field, condition.Upper);
            var key = field.Kind == PartitionFieldKind.Bool
                ? (condition.Operator == PartitionOperator.Is ? "partition_summary_yes" : "partition_summary_no")
                : "partition_summary_" + condition.Operator.ToString().ToLowerInvariant();
            return text(key, new object[] { name, target, upper });
        }).ToList();
        return parts.Count == 0
            ? text("partition_summary_empty", Array.Empty<object>())
            : string.Join(text("partition_summary_and", Array.Empty<object>()), parts);
    }
}
