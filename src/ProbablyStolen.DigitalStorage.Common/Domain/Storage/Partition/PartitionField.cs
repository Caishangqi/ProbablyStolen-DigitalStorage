using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>How a partition field's value is compared.</summary>
public enum PartitionFieldKind
{
    /// <summary>An item identifier, compared with is / is not.</summary>
    ItemId = 0,

    /// <summary>A set of item types, compared with contains / does not contain.</summary>
    TypeSet = 1,

    /// <summary>One of a fixed set of stable values, compared with is / is not.</summary>
    Enum = 2,

    /// <summary>A yes/no attribute; "is" means yes and "is not" means no, with no target value.</summary>
    Bool = 3,

    /// <summary>A whole number with comparison and range operators.</summary>
    Number = 4
}

/// <summary>A partition condition operator.</summary>
public enum PartitionOperator
{
    /// <summary>Equal to the target, or yes for a boolean.</summary>
    Is = 0,

    /// <summary>Not equal to the target, or no for a boolean.</summary>
    IsNot = 1,

    /// <summary>The set contains the target.</summary>
    Contains = 2,

    /// <summary>The set does not contain the target.</summary>
    NotContains = 3,

    /// <summary>Number equal.</summary>
    Eq = 4,

    /// <summary>Number not equal.</summary>
    Ne = 5,

    /// <summary>Number greater than.</summary>
    Gt = 6,

    /// <summary>Number greater than or equal.</summary>
    Ge = 7,

    /// <summary>Number less than.</summary>
    Lt = 8,

    /// <summary>Number less than or equal.</summary>
    Le = 9,

    /// <summary>Number within an inclusive range.</summary>
    Range = 10
}

/// <summary>An attribute visible to the player that partition conditions may test.</summary>
public sealed class PartitionField
{
    internal PartitionField(string id, PartitionFieldKind kind, string nameKey, string? unitKey,
        IReadOnlyList<PartitionOperator> operators, IReadOnlyList<string>? enumValues = null)
    {
        Id = id;
        Kind = kind;
        NameKey = nameKey;
        UnitKey = unitKey;
        Operators = operators;
        EnumValues = enumValues;
    }

    /// <summary>Gets the stable identifier saved in rules.</summary>
    public string Id { get; }

    /// <summary>Gets how values compare.</summary>
    public PartitionFieldKind Kind { get; }

    /// <summary>Gets the localization key of the display name.</summary>
    public string NameKey { get; }

    /// <summary>Gets the localization key of the unit, if any.</summary>
    public string? UnitKey { get; }

    /// <summary>Gets the operators the field supports; the first is the default.</summary>
    public IReadOnlyList<PartitionOperator> Operators { get; }

    /// <summary>Gets the stable values of an enum field.</summary>
    public IReadOnlyList<string>? EnumValues { get; }

    /// <summary>Gets whether conditions on this field carry a target value.</summary>
    public bool HasTarget => Kind != PartitionFieldKind.Bool;

    /// <summary>Gets whether an operator applies to this field.</summary>
    public bool Supports(PartitionOperator op) => Operators.Contains(op);
}

/// <summary>The fields players may build conditions on. Internal item data is never exposed automatically.</summary>
public static class PartitionFieldRegistry
{
    /// <summary>The item identifier field.</summary>
    public const string Item = "item";

    /// <summary>The item type set field.</summary>
    public const string Type = "type";

    /// <summary>The contraband level field.</summary>
    public const string Legality = "legality";

    /// <summary>The stolen status field.</summary>
    public const string Stolen = "stolen";

    /// <summary>The stolen heat field, in percent; a known legal item has heat 0.</summary>
    public const string Heat = "heat";

    /// <summary>The prefix of exposed item feature fields; the feature's own identifier follows it.</summary>
    public const string FeaturePrefix = "feature:";

    /// <summary>The stable contraband levels, mild to severe.</summary>
    public static IReadOnlyList<string> LegalityValues { get; } = new[] { "none", "low", "mid", "high", "critical" };

    private static readonly PartitionOperator[] EqualityOps = { PartitionOperator.Is, PartitionOperator.IsNot };

    private static readonly PartitionOperator[] NumberOps =
    {
        PartitionOperator.Eq, PartitionOperator.Ne, PartitionOperator.Gt, PartitionOperator.Ge,
        PartitionOperator.Lt, PartitionOperator.Le, PartitionOperator.Range
    };

    private static readonly PartitionField[] Fixed =
    {
        new(Item, PartitionFieldKind.ItemId, "partition_field_item", null, EqualityOps),
        new(Type, PartitionFieldKind.TypeSet, "partition_field_type", null,
            new[] { PartitionOperator.Contains, PartitionOperator.NotContains }),
        new(Legality, PartitionFieldKind.Enum, "partition_field_legality", null, EqualityOps, LegalityValues),
        new(Stolen, PartitionFieldKind.Bool, "partition_field_stolen", null, EqualityOps),
        new(Heat, PartitionFieldKind.Number, "partition_field_heat", "partition_unit_percent", NumberOps)
    };

    /// <summary>Gets the fixed fields in menu order.</summary>
    public static IReadOnlyList<PartitionField> All => Fixed;

    /// <summary>Gets a field by identifier, including <c>feature:&lt;id&gt;</c> fields, or null.</summary>
    public static PartitionField? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var field in Fixed)
            if (string.Equals(field.Id, id, StringComparison.Ordinal)) return field;
        return IsFeature(id!) ? new PartitionField(id!, PartitionFieldKind.Bool, "partition_field_feature", null, EqualityOps) : null;
    }

    /// <summary>Gets the field identifier of an item feature.</summary>
    public static string FeatureField(string featureId) =>
        string.IsNullOrEmpty(featureId) ? throw new ArgumentException("A feature needs an identifier.", nameof(featureId))
            : FeaturePrefix + featureId;

    private static bool IsFeature(string id) =>
        id.StartsWith(FeaturePrefix, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(id.Substring(FeaturePrefix.Length));
}
