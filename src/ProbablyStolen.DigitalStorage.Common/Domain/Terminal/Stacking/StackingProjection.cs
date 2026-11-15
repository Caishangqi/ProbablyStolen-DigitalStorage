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
/// Maps an entry to its grouping key. Raw fields set to ignore are removed from the identity, derived fields that are
/// not ignored are added, and everything else stays exact. When anything is unknown the entry keeps its exact key, so
/// a missing value never merges items.
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
    /// Lists the ignore rules that act on an item, in rule order: registered fields that describe it and that the player
    /// can see on it. Derived fields always count. Empty when the configuration does not merge.
    /// </summary>
    public static IReadOnlyList<StackingField> AppliedIgnoredFields(ItemKey key, StackingConfig config)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (!config.Enabled || !config.HasIgnoreRule) return Array.Empty<StackingField>();
        var input = CanonicalMask.Decode(key);
        if (input == null) return Array.Empty<StackingField>();
        var fields = new List<StackingField>();
        foreach (var rule in config.Rules)
        {
            if (rule.Mode != StackingMode.Ignore) continue;
            var field = StackingFieldCatalog.Find(rule.FieldId);
            if (field == null) continue;
            if (field.Kind == StackingFieldKind.Derived || field.Applies(input) && field.IsVisible(input)) fields.Add(field);
        }

        return fields;
    }

    private static Masked Mask(ItemKey key, StackingConfig config, StackingAttributes attributes)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        if (!config.Enabled || !config.HasIgnoreRule) return Masked.Exact("no_ignore_rule");
        var input = CanonicalMask.Decode(key);
        if (input == null) return Masked.Exact("not_canonical");

        var ignored = new HashSet<string>(config.Rules.Where(rule => rule.Mode == StackingMode.Ignore)
            .Select(rule => rule.FieldId), StringComparer.Ordinal);
        var paths = StackingOwnedPaths.None;
        var derived = new List<long>();
        foreach (var fieldId in ignored.OrderBy(id => id, StringComparer.Ordinal))
        {
            var field = StackingFieldCatalog.Find(fieldId);
            if (field == null || field.Kind == StackingFieldKind.Derived) continue;
            var applies = field.Applies(input);
            // An item the field describes and one it does not stay apart, so absence is never read as a default.
            derived.Add(applies ? 1 : 0);
            if (!applies) continue;
            if (!field.IsVisible(input)) return Masked.Exact("hidden:" + field.Id);
            paths = paths.Union(field.OwnedPaths);
        }

        foreach (var field in StackingFieldCatalog.FixedFields.Where(field => field.Kind == StackingFieldKind.Derived))
        {
            if (ignored.Contains(field.Id) || !field.Applies(input)) continue;
            if (!attributes.TryGet(field.Id, out var value)) return Masked.Exact("unread:" + field.Id);
            derived.Add(value);
        }

        return new Masked(CanonicalMask.Strip(input, paths), derived.ToArray(), null);
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
        internal Masked(CanonicalIdentityInput? input, long[] derived, string? reason)
        {
            Input = input;
            Derived = derived;
            Reason = reason;
        }

        internal CanonicalIdentityInput? Input { get; }
        internal long[] Derived { get; }
        internal string? Reason { get; }

        internal static Masked Exact(string reason) => new(null, Array.Empty<long>(), reason);
    }
}
