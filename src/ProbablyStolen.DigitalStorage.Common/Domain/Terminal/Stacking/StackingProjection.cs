using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>Derived values read from one stored item, such as its estimated value. Detached from game objects.</summary>
public sealed class StackingAttributes
{
    private readonly Dictionary<string, long> _values;

    /// <summary>Creates attributes.</summary>
    public StackingAttributes(IReadOnlyDictionary<string, long> values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        _values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pair in values) _values[pair.Key] = pair.Value;
    }

    /// <summary>Gets attributes with no values; every derived field then counts as unreadable.</summary>
    public static StackingAttributes Empty { get; } = new(new Dictionary<string, long>());

    /// <summary>Reads a derived value.</summary>
    public bool TryGet(string fieldId, out long value) => _values.TryGetValue(fieldId, out value);
}

/// <summary>
/// The grouping key of one entry. Entries with equal keys share a display group. An exact key holds the entry's own
/// identity, so it only equals itself.
/// </summary>
public sealed class StackingGroupKey : IEquatable<StackingGroupKey>
{
    private readonly byte[] _bytes;
    private readonly long[] _derived;
    private readonly int _hash;
    private string? _stableId;

    private StackingGroupKey(bool exact, string identifier, byte[] bytes, long[] derived)
    {
        IsExact = exact;
        Identifier = identifier;
        _bytes = bytes;
        _derived = derived;
        var hash = (exact ? 1 : 2) * 31 + StringComparer.Ordinal.GetHashCode(identifier);
        foreach (var value in bytes) hash = unchecked(hash * 31 + value);
        foreach (var value in derived) hash = unchecked(hash * 31 + value.GetHashCode());
        _hash = hash;
    }

    /// <summary>Gets whether the entry keeps its exact identity.</summary>
    public bool IsExact { get; }

    /// <summary>Gets the item definition identifier every member shares.</summary>
    public string Identifier { get; }

    /// <summary>Gets a stable text form for display group identifiers; equal keys give equal text.</summary>
    public string ToStableId() => _stableId ??= ComputeStableId();

    private string ComputeStableId()
    {
        using var sha = SHA256.Create();
        var payload = new List<byte>(_bytes);
        foreach (var value in _derived) payload.AddRange(BitConverter.GetBytes(value));
        var digest = sha.ComputeHash(payload.ToArray());
        var builder = new StringBuilder(IsExact ? "x:" : "s:").Append(Identifier).Append(':');
        foreach (var b in digest) builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    /// <inheritdoc />
    public bool Equals(StackingGroupKey? other) =>
        other != null && _hash == other._hash && IsExact == other.IsExact && Identifier == other.Identifier &&
        _bytes.AsSpan().SequenceEqual(other._bytes) && _derived.AsSpan().SequenceEqual(other._derived);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StackingGroupKey);

    /// <inheritdoc />
    public override int GetHashCode() => _hash;

    internal static StackingGroupKey Exact(ItemKey key)
    {
        var bytes = key.IsCanonical
            ? key.CopyCanonicalState()
            : Encoding.UTF8.GetBytes(TerminalIdentityText(key));
        return new StackingGroupKey(true, key.Identifier, bytes, Array.Empty<long>());
    }

    internal static StackingGroupKey Masked(string identifier, byte[] bytes, long[] derived) =>
        new(false, identifier, bytes, derived);

    private static string TerminalIdentityText(ItemKey key) =>
        string.Join("\u0001", key.Components.Select(component => component.Key + "\u0002" + component.Value));
}

/// <summary>
/// Maps an entry to its grouping key under the effective rule of its item definition: its item rule joined with the
/// global rows. Raw fields set to ignore are removed from the identity, derived fields that are not ignored are added,
/// and everything else stays exact. An item without an active rule, or with anything unknown, keeps its exact key, so a
/// missing value never merges items.
/// </summary>
public static class StackingProjection
{
    /// <summary>Computes the grouping key of one entry under a configuration.</summary>
    public static StackingGroupKey Compute(ItemKey key, StackingConfig config, StackingAttributes attributes)
    {
        var masked = Mask(key, config, attributes);
        if (masked.Input == null) return StackingGroupKey.Exact(key);
        var bytes = CanonicalMask.Encode(masked.Input);
        return bytes == null ? StackingGroupKey.Exact(key) : StackingGroupKey.Masked(key.Identifier, bytes, masked.Derived);
    }

    /// <summary>
    /// Names the parts that still keep two entries of one item apart under a configuration: "exact" with a reason when
    /// an entry cannot be grouped at all, otherwise sections such as tag keys or feature identifiers. Only names are
    /// returned, never values.
    /// </summary>
    public static string Explain(ItemKey left, StackingAttributes leftAttributes, ItemKey right,
        StackingAttributes rightAttributes, StackingConfig config)
    {
        var a = Mask(left, config, leftAttributes);
        var b = Mask(right, config, rightAttributes);
        if (a.Input == null || b.Input == null) return "exact:" + (a.Reason ?? b.Reason);
        var parts = new List<string>();
        if (!a.Derived.SequenceEqual(b.Derived)) parts.Add("derived");
        if (a.Input.UnitBaseValue != b.Input.UnitBaseValue || a.Input.UnitValue != b.Input.UnitValue ||
            a.Input.LateUnitValue != b.Input.LateUnitValue || a.Input.BackupUnitValue != b.Input.BackupUnitValue)
            parts.Add("values");
        if (!a.Input.ItemTypes.SequenceEqual(b.Input.ItemTypes)) parts.Add("types");
        if (a.Input.Name != b.Input.Name || a.Input.ShortDescription != b.Input.ShortDescription ||
            a.Input.FlavorText != b.Input.FlavorText) parts.Add("text");
        if (a.Input.SpriteAtlasPath != b.Input.SpriteAtlasPath || a.Input.SpritePath != b.Input.SpritePath) parts.Add("sprite");
        var tags = DifferentNames(a.Input.Tags, b.Input.Tags, tag => tag.Key, Encoded);
        if (tags.Count > 0) parts.Add("tags[" + string.Join(",", tags) + "]");
        var features = DifferentNames(a.Input.Features, b.Input.Features, feature => feature.Identifier, Encoded);
        if (features.Count > 0) parts.Add("features[" + string.Join(",", features) + "]");
        if (!SameNode(a.Input.ContentWindow, b.Input.ContentWindow, a.Input, b.Input, true)) parts.Add("content");
        if (!SameNode(a.Input.ExamineWindow, b.Input.ExamineWindow, a.Input, b.Input, false)) parts.Add("examine");
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    /// <summary>
    /// Lists configurable field identifiers that differ between two identities after the active ignore rules. Values are
    /// never returned; unclassified or unreadable state is represented by the item identity field.
    /// </summary>
    public static IReadOnlyList<string> Differences(ItemKey left, StackingAttributes leftAttributes, ItemKey right,
        StackingAttributes rightAttributes, StackingConfig config)
    {
        TryGetDifferences(left, leftAttributes, right, rightAttributes, config, out var differences);
        return differences;
    }

    /// <summary>Compares two identities, returning false when an exact fallback prevents a complete comparison.</summary>
    public static bool TryGetDifferences(ItemKey left, StackingAttributes leftAttributes, ItemKey right,
        StackingAttributes rightAttributes, StackingConfig config, out IReadOnlyList<string> result)
    {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));
        if (leftAttributes == null) throw new ArgumentNullException(nameof(leftAttributes));
        if (rightAttributes == null) throw new ArgumentNullException(nameof(rightAttributes));
        if (config == null) throw new ArgumentNullException(nameof(config));

        var a = MaskForComparison(left, leftAttributes, config.EffectiveRule(left.Identifier), config.Advanced);
        var b = MaskForComparison(right, rightAttributes, config.EffectiveRule(right.Identifier), config.Advanced);
        if (a.Input == null || b.Input == null)
        {
            result = left.Equals(right) ? Array.Empty<string>() : new[] { "item_identity" };
            return false;
        }

        var differences = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var fieldId in a.DerivedFields.Concat(b.DerivedFields).Distinct(StringComparer.Ordinal))
        {
            var leftIndex = Array.IndexOf(a.DerivedFields, fieldId);
            var rightIndex = Array.IndexOf(b.DerivedFields, fieldId);
            if (leftIndex < 0 || rightIndex < 0 || a.Derived[leftIndex] != b.Derived[rightIndex]) differences.Add(fieldId);
        }

        if (a.Input.Identifier != b.Input.Identifier) differences.Add("item_identity");
        if (a.Input.UnitBaseValue != b.Input.UnitBaseValue || a.Input.UnitValue != b.Input.UnitValue ||
            a.Input.LateUnitValue != b.Input.LateUnitValue || a.Input.BackupUnitValue != b.Input.BackupUnitValue)
            differences.Add(StackingFieldCatalog.PriceComposition);
        if (!a.Input.ItemTypes.SequenceEqual(b.Input.ItemTypes) || a.Input.Name != b.Input.Name ||
            a.Input.ShortDescription != b.Input.ShortDescription || a.Input.FlavorText != b.Input.FlavorText ||
            a.Input.SpriteAtlasPath != b.Input.SpriteAtlasPath || a.Input.SpritePath != b.Input.SpritePath)
            differences.Add("item_identity");

        foreach (var tagKey in DifferentNames(a.Input.Tags, b.Input.Tags, tag => tag.Key, Encoded))
            differences.Add(StackingFieldCatalog.TagFieldId(tagKey));
        foreach (var featureId in DifferentNames(a.Input.Features, b.Input.Features, feature => feature.Identifier, Encoded))
        {
            var leftFeature = a.Input.Features.FirstOrDefault(feature => feature.Identifier == featureId);
            var rightFeature = b.Input.Features.FirstOrDefault(feature => feature.Identifier == featureId);
            var present = leftFeature ?? rightFeature;
            if (present == null)
            {
                differences.Add("item_identity");
                continue;
            }

            if (!config.Advanced &&
                (leftFeature != null && (!leftFeature.IsFeatureExposed || leftFeature.IsDisabled) ||
                 rightFeature != null && (!rightFeature.IsFeatureExposed || rightFeature.IsDisabled)))
            {
                differences.Add(StackingFieldCatalog.HiddenFeature);
                continue;
            }

            var owner = StackingFieldCatalog.FixedFields.FirstOrDefault(field => field.OwnedPaths.FeatureIds.Contains(featureId));
            differences.Add(owner?.Id ?? StackingFieldCatalog.FeaturePrefix + featureId);
        }

        if (!SameNode(a.Input.ContentWindow, b.Input.ContentWindow, a.Input, b.Input, true))
            differences.Add(StackingFieldCatalog.ContainerContent);
        if (!SameNode(a.Input.ExamineWindow, b.Input.ExamineWindow, a.Input, b.Input, false))
            differences.Add("item_identity");
        result = differences.ToArray();
        return true;
    }

    /// <summary>Finds the differences that can distinguish any of the selected identities.</summary>
    public static IReadOnlyCollection<string> CollectDifferences(
        IReadOnlyList<(ItemKey Key, StackingAttributes Attributes)> items, StackingConfig config)
    {
        var differences = new SortedSet<string>(StringComparer.Ordinal);
        var comparable = new List<(ItemKey Key, StackingAttributes Attributes)>();
        foreach (var item in items)
        {
            if (TryGetDifferences(item.Key, item.Attributes, item.Key, item.Attributes, config, out _))
                comparable.Add(item);
            else
                differences.Add("item_identity");
        }

        var compareRemaining = false;
        for (var index = 1; index < comparable.Count; index++)
            compareRemaining |= Compare(comparable[0], comparable[index]);

        // A hidden feature on the first item can obscure distinct exposed values on the other items.
        if (compareRemaining)
        {
            for (var left = 1; left < comparable.Count; left++)
            {
                for (var right = left + 1; right < comparable.Count; right++)
                    Compare(comparable[left], comparable[right]);
            }
        }

        return differences;

        bool Compare((ItemKey Key, StackingAttributes Attributes) left,
            (ItemKey Key, StackingAttributes Attributes) right)
        {
            if (!TryGetDifferences(left.Key, left.Attributes, right.Key, right.Attributes, config, out var fields))
            {
                differences.Add("item_identity");
                return true;
            }

            differences.UnionWith(fields);
            return fields.Contains("item_identity", StringComparer.Ordinal) ||
                fields.Contains(StackingFieldCatalog.HiddenFeature, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Lists the ignore rules whose unprotected fields differ among the members of one projected group, in rule order.
    /// Derived fields are included only when captured values show a difference.
    /// </summary>
    public static IReadOnlyList<StackingField> AppliedIgnoredFields(
        IReadOnlyList<(ItemKey Key, StackingAttributes Attributes)> members, StackingConfig config)
    {
        if (members == null) throw new ArgumentNullException(nameof(members));
        if (config == null) throw new ArgumentNullException(nameof(config));
        var itemRule = members.Count < 2 ? null : config.EffectiveRule(members[0].Key.Identifier);
        if (itemRule == null || !itemRule.IsActive) return Array.Empty<StackingField>();
        var inputs = new CanonicalIdentityInput[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            var input = CanonicalMask.Decode(members[i].Key);
            if (input == null || input.Identifier != members[0].Key.Identifier) return Array.Empty<StackingField>();
            inputs[i] = input;
        }

        // Within one projected group, the applicability of a Match field overlapping ignored paths is shared.
        var rules = itemRule.Rules.Select(rule => (rule.Mode, Field: StackingFieldCatalog.Find(rule.FieldId))).ToArray();
        var matchedPaths = StackingOwnedPaths.None;
        foreach (var (mode, field) in rules)
        {
            if (mode == StackingMode.Match && field != null && !field.OwnedPaths.IsEmpty && field.Applies(inputs[0]))
                matchedPaths = matchedPaths.Union(field.OwnedPaths);
        }

        var presentTags = new HashSet<string>(inputs.SelectMany(input => input.Tags).Select(tag => tag.Key), StringComparer.Ordinal);
        var presentFeatures = new HashSet<string>(inputs.SelectMany(input => input.Features).Select(feature => feature.Identifier),
            StringComparer.Ordinal);
        var hasContentWindow = inputs.Any(input => input.ContentWindow != null);
        var fields = new List<StackingField>();
        foreach (var (mode, field) in rules)
        {
            if (mode != StackingMode.Ignore || field == null ||
                !inputs.Any(input => field.Applies(input) && (config.Advanced || field.IsVisible(input)))) continue;
            if (field.Kind == StackingFieldKind.Derived)
            {
                if (HasDerivedDifference(members, field.Id)) fields.Add(field);
                continue;
            }

            var paths = field.OwnedPaths.Without(matchedPaths);
            var hasOwnedData = paths.ValueFields || paths.ContentWindow && hasContentWindow ||
                paths.TagKeys.Any(presentTags.Contains) || paths.FeatureIds.Any(presentFeatures.Contains);
            if (hasOwnedData && HasOwnedDifference(inputs, paths)) fields.Add(field);
        }

        return fields;
    }

    private static bool HasDerivedDifference(IReadOnlyList<(ItemKey Key, StackingAttributes Attributes)> members, string fieldId)
    {
        long? first = null;
        foreach (var member in members)
        {
            if (!member.Attributes.TryGet(fieldId, out var value)) continue;
            if (first.HasValue && first.Value != value) return true;
            first = value;
        }

        return false;
    }

    private static bool HasOwnedDifference(IReadOnlyList<CanonicalIdentityInput> inputs, StackingOwnedPaths paths)
    {
        var first = CanonicalMask.EncodeOwned(inputs[0], paths);
        if (first == null) return false;
        for (var i = 1; i < inputs.Count; i++)
        {
            var other = CanonicalMask.EncodeOwned(inputs[i], paths);
            if (other != null && !first.AsSpan().SequenceEqual(other)) return true;
        }

        return false;
    }

    private static Masked Mask(ItemKey key, StackingConfig config, StackingAttributes attributes)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        return Mask(key, config.EffectiveRule(key.Identifier), config.Advanced, attributes);
    }

    /// <summary>
    /// Masks one identity under an item rule. A hidden feature row applies only with the advanced option; without it the
    /// row pauses and its data stays compared. A match row always protects its data, hidden or not.
    /// </summary>
    private static Masked Mask(ItemKey key, StackingItemRule? itemRule, bool advanced, StackingAttributes attributes)
    {
        if (itemRule == null || !itemRule.IsActive) return Masked.Exact("no_ignore_rule");
        var input = CanonicalMask.Decode(key);
        if (input == null) return Masked.Exact("not_canonical");

        var ignoredRules = itemRule.Rules.Where(rule => rule.Mode == StackingMode.Ignore)
            .OrderBy(rule => rule.FieldId, StringComparer.Ordinal).ToArray();
        var matchedRules = itemRule.Rules.Where(rule => rule.Mode == StackingMode.Match)
            .OrderBy(rule => rule.FieldId, StringComparer.Ordinal).ToArray();
        var ignored = new HashSet<string>(ignoredRules.Select(rule => rule.FieldId), StringComparer.Ordinal);
        var paths = StackingOwnedPaths.None;
        var matchedPaths = StackingOwnedPaths.None;
        var derived = new List<long>();
        var derivedFields = new List<string>();
        foreach (var rule in ignoredRules)
        {
            var field = StackingFieldCatalog.Find(rule.FieldId);
            if (field == null || field.Kind == StackingFieldKind.Derived) continue;
            var applies = field.Applies(input);
            if (field.Kind != StackingFieldKind.Feature)
            {
                derived.Add(applies ? 1 : 0);
                derivedFields.Add(field.Id);
            }

            if (!applies || !advanced && !field.IsVisible(input)) continue;
            paths = paths.Union(field.OwnedPaths);
        }

        foreach (var rule in matchedRules)
        {
            var field = StackingFieldCatalog.Find(rule.FieldId);
            if (field == null || field.Kind == StackingFieldKind.Derived) continue;
            var overlap = paths.Intersect(field.OwnedPaths);
            if (overlap.IsEmpty) continue;
            var applies = field.Applies(input);
            derived.Add(applies ? 1 : 0);
            derivedFields.Add(field.Id);
            if (!applies) continue;
            matchedPaths = matchedPaths.Union(overlap);
        }

        paths = paths.Without(matchedPaths);
        foreach (var field in StackingFieldCatalog.FixedFields.Where(field => field.Kind == StackingFieldKind.Derived))
        {
            if (ignored.Contains(field.Id) || !field.Applies(input)) continue;
            if (!attributes.TryGet(field.Id, out var value)) return Masked.Exact("unread:" + field.Id);
            derived.Add(value);
            derivedFields.Add(field.Id);
        }

        return new Masked(CanonicalMask.Strip(input, paths), derived.ToArray(), derivedFields.ToArray(), null);
    }

    private static Masked MaskForComparison(ItemKey key, StackingAttributes attributes, StackingItemRule? itemRule,
        bool advanced)
    {
        if (itemRule != null && itemRule.IsActive) return Mask(key, itemRule, advanced, attributes);
        var input = CanonicalMask.Decode(key);
        return input == null ? Masked.Exact("not_canonical") : new Masked(input, Array.Empty<long>(), Array.Empty<string>(), null);
    }

    private static List<string> DifferentNames<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, Func<T, string> name,
        Func<T, string> content)
    {
        var a = left.ToDictionary(name, content, StringComparer.Ordinal);
        var b = right.ToDictionary(name, content, StringComparer.Ordinal);
        return a.Keys.Union(b.Keys).Where(key => !a.TryGetValue(key, out var x) || !b.TryGetValue(key, out var y) || x != y)
            .OrderBy(key => key, StringComparer.Ordinal).ToList();
    }

    private static bool SameNode(CanonicalIdentityNode? left, CanonicalIdentityNode? right, CanonicalIdentityInput a,
        CanonicalIdentityInput b, bool content)
    {
        if (left == null || right == null) return left == null && right == null;
        var bare = new Func<CanonicalIdentityInput, CanonicalIdentityNode, CanonicalIdentityInput>((owner, node) =>
            new CanonicalIdentityInput("x", Array.Empty<string>(), Array.Empty<CanonicalIdentityTag>(),
                Array.Empty<CanonicalIdentityFeature>(), contentWindow: content ? node : null,
                examineWindow: content ? null : node));
        var x = CanonicalMask.Encode(bare(a, left));
        var y = CanonicalMask.Encode(bare(b, right));
        return x != null && y != null && x.AsSpan().SequenceEqual(y);
    }

    private static string Encoded(CanonicalIdentityTag tag) =>
        string.Join("\u0001", tag.Identifier, tag.IdentifierName, tag.Enabled, tag.ValueString, tag.ValueInt, tag.ValueFloatBits,
            tag.ValueLong, tag.ValueDoubleBits, tag.ValueBool);

    private static string Encoded(CanonicalIdentityFeature feature) =>
        string.Join("\u0001", feature.Category, feature.FeatureType, feature.ValueStage, feature.PublicDisplay,
            feature.ActualDisplay, feature.RemovedByTool, feature.ValueModifier, feature.InitiallyShown,
            feature.IsFeatureExposed, feature.IsExposing, feature.IsFeatureDiscovered, feature.UseCondition,
            feature.UsePreExposeValue, feature.PreExposeValueModifier, feature.IsExposable, feature.IsPublicHidden,
            feature.CustomIntValue1, feature.IsDisabled, feature.IsFeatureMatch, Condition(feature.FakeCondition),
            Condition(feature.RealCondition));

    private static string Condition(CanonicalIdentityCondition? condition) => condition == null ? "-" :
        string.Join("\u0002", condition.Display, condition.Identifier, condition.ModValue, condition.ModValueDisplay,
            condition.IsTransformative, condition.Category, condition.HiddenAsPublic, condition.CustomValue,
            condition.OnActivateActionId, condition.CompareValue);

    private readonly struct Masked
    {
        internal Masked(CanonicalIdentityInput? input, long[] derived, string[] derivedFields, string? reason)
        {
            Input = input;
            Derived = derived;
            DerivedFields = derivedFields;
            Reason = reason;
        }

        internal CanonicalIdentityInput? Input { get; }
        internal long[] Derived { get; }
        internal string[] DerivedFields { get; }
        internal string? Reason { get; }

        internal static Masked Exact(string reason) =>
            new(null, Array.Empty<long>(), Array.Empty<string>(), reason);
    }
}
