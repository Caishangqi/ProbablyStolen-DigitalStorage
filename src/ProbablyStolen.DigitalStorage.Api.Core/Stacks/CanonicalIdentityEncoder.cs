using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace ProbablyStolen.DigitalStorage.Api.Stacks;

/// <summary>Encodes detached identity facts without creating or modifying physical items.</summary>
internal static class CanonicalIdentityEncoder
{
    /// <summary>Builds a validated version 1 identity from one detached, immutable projection.</summary>
    /// <param name="input">The detached projection to encode.</param>
    /// <param name="anomalies">
    /// Receives values that are encoded as they are although the game would not write them, such as a non-finite tag
    /// value or a lone surrogate, each with its rule and place. At most <see cref="MaxAnomalies"/> are listed; one
    /// more entry with the rule <c>anomaly_overflow</c> names the place of the first one left out.
    /// </param>
    /// <exception cref="CanonicalIdentityRejectedException">The input breaks an encoding limit or format rule.</exception>
    public static ItemKey Encode(CanonicalIdentityInput input, ICollection<(string Rule, string At)>? anomalies = null)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        var writer = new Writer();
        writer.Write(input);
        var state = writer.Finish();
        writer.CopyAnomaliesTo(anomalies);
        try
        {
            return ItemKey.FromCanonical(input.Identifier, state);
        }
        catch (ArgumentException)
        {
            throw new CanonicalIdentityRejectedException("format_rule",
                CanonicalIdentityValidator.FailurePoint(state, input.Identifier) ??
                IdentityPlace.Format(input.Identifier ?? "", null, null));
        }
    }

    /// <summary>The most anomalies listed for one identity.</summary>
    internal const int MaxAnomalies = 16;

    private sealed class Writer
    {
        private const int MaxBytes = 1048576;
        private readonly List<(string Rule, string At)> _anomalies = new();
        private readonly HashSet<(string Rule, string At)> _noted = new();
        private string? _overflowAt;
        private readonly MemoryStream _stream = new();
        private readonly HashSet<object> _seen = new(ReferenceComparer.Instance);
        private readonly List<Step> _path = new();
        private int _nodes;
        private int _items;
        private int _edges;
        private int _records;
        private string _item = "";
        private string? _partPrefix;
        private string? _partName;

        internal void Write(CanonicalIdentityInput input)
        {
            Bytes(new byte[] { (byte)'D', (byte)'S', (byte)'I', (byte)'D' });
            U32(1);
            Item(input, null, 0, 0);
        }

        internal byte[] Finish() => _stream.ToArray();

        internal void CopyAnomaliesTo(ICollection<(string Rule, string At)>? target)
        {
            if (target == null) return;
            foreach (var anomaly in _anomalies) target.Add(anomaly);
            if (_overflowAt != null) target.Add(("anomaly_overflow", _overflowAt));
        }

        private void Item(CanonicalIdentityInput item, CanonicalIdentityChild? child, int itemDepth, int nodeDepth)
        {
            var outerItem = _item;
            var outerPrefix = _partPrefix;
            var outerName = _partName;
            _item = item?.Identifier ?? "";
            Part(null, null);
            Seen(item!);
            if (itemDepth > 8 || ++_items > 8192) Invalid("item_limit");
            Byte(child == null ? (byte)10 : (byte)11);
            if (child != null)
            {
                Seen(child);
                if (child.UnitCount <= 0) Invalid("unit_count");
                I32(child.UnitCount); Shape(child.OriginalShape); Shape(child.ModifiedShape);
            }

            Byte(1); String(item!.Identifier); String(item.Name); String(item.ShortDescription); String(item.FlavorText);
            I64(item.UnitBaseValue); I64(item.UnitValue); I64(item.LateUnitValue); I64(item.BackupUnitValue);
            U32((uint)item.ItemTypes.Count);
            foreach (var type in item.ItemTypes)
            {
                if (type == null) Invalid("null_entry");
                String(type);
            }
            String(item.SpriteAtlasPath); String(item.SpritePath);
            ReserveRecords(item.Tags.Count);
            foreach (var tag in item.Tags)
            {
                if (tag == null || tag.Key == null) Invalid("null_entry");
                Part("tag:", tag!.Key);
                CheckTagText(tag.Key); CheckTagText(tag.Identifier);
                CheckTagText(tag.IdentifierName); CheckTagText(tag.ValueString);
            }
            var tags = new List<CanonicalIdentityTag>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in item.Tags)
            {
                Part("tag:", tag.Key);
                Seen(tag);
                if (!keys.Add(tag.Key!)) Invalid("duplicate_tag");
                if ((tag.ValueFloatBits & 0x7f800000U) == 0x7f800000U ||
                    (tag.ValueDoubleBits & 0x7ff0000000000000UL) == 0x7ff0000000000000UL) Note("nonfinite_value");
                if (tag.Enabled || !string.IsNullOrEmpty(tag.ValueString) || tag.ValueInt != 0 ||
                    (tag.ValueFloatBits & 0x7fffffffU) != 0 || tag.ValueLong != 0 ||
                    (tag.ValueDoubleBits & 0x7fffffffffffffffUL) != 0 || tag.ValueBool) tags.Add(tag);
            }
            Part(null, null);
            ReserveEncodedTagBytes(tags);
            tags.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
            U32((uint)tags.Count);
            foreach (var tag in tags)
            {
                Part("tag:", tag.Key);
                Tag(tag);
            }
            Part(null, null);
            U32((uint)item.Features.Count);
            foreach (var feature in item.Features) Feature(feature);
            Part(null, null);
            Window(item.ContentWindow, 1, itemDepth, nodeDepth);
            Window(item.ExamineWindow, 2, itemDepth, nodeDepth);
            _item = outerItem;
            Part(outerPrefix, outerName);
        }

        private void Tag(CanonicalIdentityTag tag)
        {
            String(tag.Key); String(tag.Identifier); String(tag.IdentifierName); Bool(tag.Enabled);
            String(tag.ValueString); I32(tag.ValueInt); U32(tag.ValueFloatBits); I64(tag.ValueLong);
            U64(tag.ValueDoubleBits); Bool(tag.ValueBool);
        }

        private void Feature(CanonicalIdentityFeature? feature)
        {
            if (feature == null) Invalid("null_entry");
            Part("feature:", feature!.Identifier);
            Seen(feature); Record();
            if (feature.FeatureType < 0 || feature.FeatureType > 6) Note("feature_type");
            if (feature.ValueStage < 0 || feature.ValueStage > 2) Note("value_stage");
            I32(feature.FeatureType); I32(feature.ValueStage); U32((uint)_path.Count);
            foreach (var step in _path)
            {
                Byte(step.Kind);
                if (step.Kind == 3 || step.Kind == 4) U32(step.Index);
            }
            String(feature.PublicDisplay); String(feature.ActualDisplay); String(feature.RemovedByTool);
            I32(feature.ValueModifier); Bool(feature.InitiallyShown); Bool(feature.IsFeatureExposed);
            Bool(feature.IsExposing); Bool(feature.IsFeatureDiscovered);
            Condition(feature.FakeCondition); Condition(feature.RealCondition);
            Bool(feature.UseCondition); Bool(feature.UsePreExposeValue); I32(feature.PreExposeValueModifier);
            Bool(feature.IsExposable); Bool(feature.IsPublicHidden); I32(feature.CustomIntValue1);
            Bool(feature.IsDisabled); String(feature.Identifier); String(feature.Category); Bool(feature.IsFeatureMatch);
        }

        private void Condition(CanonicalIdentityCondition? condition)
        {
            Bool(condition != null);
            if (condition == null) return;
            Seen(condition); Record();
            String(condition.Display); String(condition.Identifier); I32(condition.ModValue); I32(condition.ModValueDisplay);
            Bool(condition.IsTransformative); String(condition.Category); Bool(condition.HiddenAsPublic);
            I32(condition.CustomValue); String(condition.OnActivateActionId); I32(condition.CompareValue);
        }

        private void Window(CanonicalIdentityNode? node, byte role, int itemDepth, int nodeDepth)
        {
            Bool(node != null);
            if (node == null) return;
            Edge(); _path.Add(new Step(role)); Node(node, itemDepth, nodeDepth + 1); _path.RemoveAt(_path.Count - 1);
        }

        private void Node(CanonicalIdentityNode? node, int itemDepth, int nodeDepth)
        {
            if (node == null) Invalid("null_entry");
            Seen(node!);
            if (nodeDepth > 16 || ++_nodes > 8192) Invalid("node_limit");
            Byte(node!.Kind);
            switch (node.Kind)
            {
                case 1: Nodes(node.Nodes, itemDepth, nodeDepth); break;
                case 2: Shape(node.Shape!); Items(node.Items, itemDepth, nodeDepth); break;
                case 3: I32(node.Width); I32(node.Height); Bool(node.AlwaysStack); Items(node.Items, itemDepth, nodeDepth); break;
                case 4:
                    I32(node.Orientation); Bool(node.SlotItem != null);
                    if (node.SlotItem != null)
                    {
                        Edge(); _path.Add(new Step(5)); Item(node.SlotItem.Item, node.SlotItem, itemDepth + 1, nodeDepth);
                        _path.RemoveAt(_path.Count - 1);
                    }
                    Nodes(node.Nodes, itemDepth, nodeDepth);
                    break;
                default: Invalid("node_kind"); break;
            }
        }

        private void Nodes(IReadOnlyList<CanonicalIdentityNode> nodes, int itemDepth, int nodeDepth)
        {
            U32((uint)nodes.Count);
            for (var i = 0; i < nodes.Count; i++)
            {
                Edge(); _path.Add(new Step(3, (uint)i)); Node(nodes[i], itemDepth, nodeDepth + 1); _path.RemoveAt(_path.Count - 1);
            }
        }

        private void Items(IReadOnlyList<CanonicalIdentityChild> items, int itemDepth, int nodeDepth)
        {
            U32((uint)items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var child = items[i];
                if (child == null) Invalid("null_entry");
                Edge(); _path.Add(new Step(4, (uint)i)); Item(child!.Item, child, itemDepth + 1, nodeDepth);
                _path.RemoveAt(_path.Count - 1);
            }
        }

        private void Shape(CanonicalIdentityShape shape)
        {
            if (shape == null) Invalid("null_entry");
            var mask = shape!.CopyMask();
            I32(shape.MinX); I32(shape.MinY); I32(shape.Width); I32(shape.Height); Bool(shape.Flipped);
            I32(shape.Orientation); Byte(shape.Outside); U32((uint)mask.Length); Bytes(mask);
        }

        private void Seen(object value)
        {
            if (!_seen.Add(value)) Invalid("shared_reference");
        }
        private void Edge() { if (++_edges > 65536) Invalid("edge_limit"); }
        private void Record() => ReserveRecords(1);
        private void ReserveRecords(int count)
        {
            if (count < 0 || count > 65536 - _records) Invalid("record_limit");
            _records += count;
        }
        // Text of every tag counts against the limits, but only text that is encoded is reported as an anomaly.
        private void CheckTagText(string? value)
        {
            if (value != null) TextBytes(value, report: false);
        }

        private void ReserveEncodedTagBytes(IReadOnlyList<CanonicalIdentityTag> tags)
        {
            var bytes = 4L;
            foreach (var tag in tags)
            {
                Part("tag:", tag.Key);
                bytes += EncodedStringBytes(tag.Key) + EncodedStringBytes(tag.Identifier) +
                    EncodedStringBytes(tag.IdentifierName) + 1 + EncodedStringBytes(tag.ValueString) + 4 + 4 + 8 + 8 + 1;
                if (bytes > MaxBytes - _stream.Length) Invalid("size_limit");
            }
        }

        private int EncodedStringBytes(string? value) => value == null ? 1 : checked(5 + TextBytes(value, report: false));

        private int TextBytes(string value, bool report)
        {
            var length = Wtf8.GetByteCount(value, out var loneSurrogate);
            if (loneSurrogate && report) Note("invalid_text");
            if (length > 65536) Invalid("text_too_long");
            return length;
        }
        private void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);
        private void Byte(byte value)
        {
            if (_stream.Length >= MaxBytes) Invalid("size_limit");
            _stream.WriteByte(value);
        }
        private void Bytes(byte[] bytes)
        {
            if (bytes.Length > MaxBytes - _stream.Length) Invalid("size_limit");
            _stream.Write(bytes, 0, bytes.Length);
        }
        private void U32(uint value)
        {
            Byte((byte)(value >> 24)); Byte((byte)(value >> 16)); Byte((byte)(value >> 8)); Byte((byte)value);
        }
        private void I32(int value) => U32(unchecked((uint)value));
        private void U64(ulong value) { U32((uint)(value >> 32)); U32((uint)value); }
        private void I64(long value) => U64(unchecked((ulong)value));
        private void String(string? value)
        {
            if (value == null) { Byte(0); return; }
            var length = TextBytes(value, report: true);
            if (length > MaxBytes - _stream.Length - 5) Invalid("size_limit");
            Byte(1); U32((uint)length); Bytes(Wtf8.GetBytes(value));
        }
        // The place is formatted only when a rule is met, so reading an ordinary identity allocates no place text.
        private void Part(string? prefix, string? name)
        {
            _partPrefix = prefix;
            _partName = name;
        }

        private string Place => IdentityPlace.Format(_item, _partPrefix, _partName);

        private void Note(string rule)
        {
            if (_overflowAt != null) return;
            var note = (rule, Place);
            if (!_noted.Add(note)) return;
            if (_anomalies.Count < MaxAnomalies) _anomalies.Add(note);
            else _overflowAt = note.Place;
        }

        private void Invalid(string rule) => throw new CanonicalIdentityRejectedException(rule, Place);
    }

    private readonly struct Step
    {
        internal Step(byte kind, uint index = 0) { Kind = kind; Index = index; }
        internal byte Kind { get; }
        internal uint Index { get; }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
