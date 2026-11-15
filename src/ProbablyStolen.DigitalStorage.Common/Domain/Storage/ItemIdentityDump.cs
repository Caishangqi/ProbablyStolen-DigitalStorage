using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// Writes an item identity as indented JSON for diagnostics: everything the identity compares, values included. It is
/// meant for an opt-in diagnostic log only, never for ordinary logs, and is cut off at a length limit.
/// </summary>
public static class ItemIdentityDump
{
    /// <summary>The default length limit.</summary>
    public const int DefaultMaxLength = 16384;

    /// <summary>Returns the JSON text of an identity, or a short note when it has no canonical state.</summary>
    public static string ToJson(ItemKey key, int maxLength = DefaultMaxLength)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (!key.IsCanonical) return "{\"identifier\":" + Quote(key.Identifier) + ",\"canonical\":false}";
        var input = CanonicalIdentityValidator.Decode(key.CopyCanonicalState(), key.Identifier);
        if (input == null) return "{\"identifier\":" + Quote(key.Identifier) + ",\"readable\":false}";
        var writer = new Writer();
        writer.Item(input, 0);
        var text = writer.ToString();
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "\n... (cut at " + maxLength + " characters)";
    }

    private static string Quote(string? value)
    {
        if (value == null) return "null";
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(c)) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    private sealed class Writer
    {
        private readonly StringBuilder _text = new();

        public override string ToString() => _text.ToString();

        internal void Item(CanonicalIdentityInput item, int depth)
        {
            Open(depth);
            Field(depth, "identifier", Quote(item.Identifier));
            Field(depth, "name", Quote(item.Name));
            Field(depth, "shortDescription", Quote(item.ShortDescription));
            Field(depth, "flavorText", Quote(item.FlavorText));
            Field(depth, "unitBaseValue", Number(item.UnitBaseValue));
            Field(depth, "unitValue", Number(item.UnitValue));
            Field(depth, "lateUnitValue", Number(item.LateUnitValue));
            Field(depth, "backupUnitValue", Number(item.BackupUnitValue));
            Field(depth, "itemTypes", "[" + string.Join(", ", Map(item.ItemTypes, Quote)) + "]");
            Field(depth, "spriteAtlasPath", Quote(item.SpriteAtlasPath));
            Field(depth, "spritePath", Quote(item.SpritePath));
            List(depth, "tags", item.Tags, Tag);
            List(depth, "features", item.Features, Feature);
            Node(depth, "contentWindow", item.ContentWindow);
            Node(depth, "examineWindow", item.ExamineWindow, last: true);
            Close(depth);
        }

        private void Node(int depth, string name, CanonicalIdentityNode? node, bool last = false)
        {
            Indent(depth + 1).Append(Quote(name)).Append(": ");
            if (node == null) _text.Append("null");
            else WriteNode(node, depth + 1);
            _text.Append(last ? "\n" : ",\n");
        }

        private void WriteNode(CanonicalIdentityNode node, int depth)
        {
            _text.Append("{ \"kind\": ").Append(node.Kind switch { 1 => "\"window\"", 2 => "\"grid\"", 3 => "\"scrollable\"", _ => "\"slot\"" });
            if (node.Kind == 3) _text.Append(", \"width\": ").Append(node.Width).Append(", \"height\": ").Append(node.Height)
                .Append(", \"alwaysStack\": ").Append(node.AlwaysStack ? "true" : "false");
            if (node.Kind == 4) _text.Append(", \"orientation\": ").Append(node.Orientation);
            if (node.Nodes.Count > 0)
            {
                _text.Append(", \"nodes\": [\n");
                for (var i = 0; i < node.Nodes.Count; i++)
                {
                    Indent(depth + 1);
                    WriteNode(node.Nodes[i], depth + 1);
                    _text.Append(i + 1 < node.Nodes.Count ? ",\n" : "\n");
                }

                Indent(depth).Append(']');
            }

            var children = new List<CanonicalIdentityChild>(node.Items);
            if (node.SlotItem != null) children.Add(node.SlotItem);
            if (children.Count > 0)
            {
                _text.Append(", \"items\": [\n");
                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    Indent(depth + 1).Append("{ \"unitCount\": ").Append(child.UnitCount)
                        .Append(", \"at\": [").Append(child.ModifiedShape.MinX).Append(", ").Append(child.ModifiedShape.MinY)
                        .Append("], \"item\":\n");
                    Item(child.Item, depth + 2);
                    Indent(depth + 1).Append(i + 1 < children.Count ? "},\n" : "}\n");
                }

                Indent(depth).Append(']');
            }

            _text.Append(" }");
        }

        private static string Tag(CanonicalIdentityTag tag) =>
            "{ \"key\": " + Quote(tag.Key) + ", \"enabled\": " + Bool(tag.Enabled) + ", \"string\": " + Quote(tag.ValueString) +
            ", \"int\": " + tag.ValueInt.ToString(CultureInfo.InvariantCulture) +
            ", \"float\": " + BitConverter.ToSingle(BitConverter.GetBytes(tag.ValueFloatBits), 0).ToString("R", CultureInfo.InvariantCulture) +
            ", \"long\": " + Number(tag.ValueLong) +
            ", \"double\": " + BitConverter.Int64BitsToDouble(unchecked((long)tag.ValueDoubleBits)).ToString("R", CultureInfo.InvariantCulture) +
            ", \"bool\": " + Bool(tag.ValueBool) + ", \"identifier\": " + Quote(tag.Identifier) +
            ", \"identifierName\": " + Quote(tag.IdentifierName) + " }";

        private static string Feature(CanonicalIdentityFeature feature) =>
            "{ \"identifier\": " + Quote(feature.Identifier) + ", \"category\": " + Quote(feature.Category) +
            ", \"type\": " + feature.FeatureType + ", \"valueStage\": " + feature.ValueStage +
            ", \"valueModifier\": " + feature.ValueModifier + ", \"preExposeValueModifier\": " + feature.PreExposeValueModifier +
            ", \"publicDisplay\": " + Quote(feature.PublicDisplay) + ", \"actualDisplay\": " + Quote(feature.ActualDisplay) +
            ", \"exposed\": " + Bool(feature.IsFeatureExposed) + ", \"discovered\": " + Bool(feature.IsFeatureDiscovered) +
            ", \"disabled\": " + Bool(feature.IsDisabled) + ", \"customInt\": " + feature.CustomIntValue1 +
            ", \"fakeCondition\": " + Condition(feature.FakeCondition) + ", \"realCondition\": " + Condition(feature.RealCondition) + " }";

        private static string Condition(CanonicalIdentityCondition? condition) => condition == null ? "null" :
            "{ \"identifier\": " + Quote(condition.Identifier) + ", \"display\": " + Quote(condition.Display) +
            ", \"modValue\": " + condition.ModValue + ", \"customValue\": " + condition.CustomValue + " }";

        private void List<T>(int depth, string name, IReadOnlyList<T> values, Func<T, string> write)
        {
            Indent(depth + 1).Append(Quote(name)).Append(": [");
            if (values.Count == 0)
            {
                _text.Append("],\n");
                return;
            }

            _text.Append('\n');
            for (var i = 0; i < values.Count; i++)
                Indent(depth + 2).Append(write(values[i])).Append(i + 1 < values.Count ? ",\n" : "\n");
            Indent(depth + 1).Append("],\n");
        }

        private void Field(int depth, string name, string value) =>
            Indent(depth + 1).Append(Quote(name)).Append(": ").Append(value).Append(",\n");

        private void Open(int depth) => Indent(depth).Append("{\n");

        private void Close(int depth) => Indent(depth).Append("}\n");

        private StringBuilder Indent(int depth) => _text.Append(' ', depth * 2);

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Bool(bool value) => value ? "true" : "false";

        private static IEnumerable<string> Map<T>(IEnumerable<T> values, Func<T, string> map)
        {
            foreach (var value in values) yield return map(value);
        }
    }
}
