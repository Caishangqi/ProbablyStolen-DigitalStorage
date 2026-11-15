using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyStolen.DigitalStorage.Api.Content;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal enum ModuleBayJsonKind { Definition, Bindings }
internal sealed class ModuleBayJsonContribution
{
    internal List<ModuleBayDefinition> Definitions { get; } = new();
    internal List<ModuleBayBinding> Bindings { get; } = new();
}

/// <summary>Strict, bounded decoding for public module-bay JSON files.</summary>
internal static class ModuleBayJsonDecoder
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly JsonLoadSettings Settings = new()
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        LineInfoHandling = LineInfoHandling.Load,
        CommentHandling = CommentHandling.Load
    };

    internal static void ValidateJson(string json)
    {
        LoadValue(json, "registry data");
        RejectCommentsAndTrailing(json);
    }

    internal static IReadOnlyList<string> BindingEntries(string json)
    {
        var root = Load(json, "module bay bindings");
        RejectCommentsAndTrailing(json);
        Properties(root, new[] { "format_version", "bindings" });
        Version(root);
        var result = new List<string>();
        foreach (var token in Array(root, "bindings"))
        {
            var binding = Object(token, "binding");
            Properties(binding, new[] { "native_item_id", "definition_id", "provider_id" }, "max_stage");
            var entry = (JObject)binding.DeepClone();
            entry.Add("format_version", 1);
            result.Add(entry.ToString(Formatting.None));
        }
        return result;
    }

    internal static ModuleBayJsonKind DetectKind(string json)
    {
        var root = Load(json, "content");
        return root.Property("bindings") != null ? ModuleBayJsonKind.Bindings : ModuleBayJsonKind.Definition;
    }

    internal static ModuleBayBinding DecodeBindingEntry(string json)
    {
        var binding = Load(json, "module bay binding"); RejectCommentsAndTrailing(json);
        Properties(binding, new[] { "format_version", "native_item_id", "definition_id", "provider_id" }, "max_stage");
        Version(binding);
        return new ModuleBayBinding(Text(binding, "native_item_id"), Text(binding, "definition_id"), Text(binding, "provider_id"),
            OptionalInteger(binding, "max_stage"));
    }

    internal static ModuleBayJsonContribution DecodeContent(string json, ModuleBayJsonKind kind)
    {
        var root = Load(json, kind.ToString());
        RejectCommentsAndTrailing(json);
        var contribution = new ModuleBayJsonContribution();
        if (kind == ModuleBayJsonKind.Bindings)
        {
            Properties(root, new[] { "format_version", "bindings" });
            Version(root);
            foreach (var token in Array(root, "bindings"))
            {
                var binding = Object(token, "binding");
                Properties(binding, new[] { "native_item_id", "definition_id", "provider_id" }, "max_stage");
                contribution.Bindings.Add(new ModuleBayBinding(Text(binding, "native_item_id"), Text(binding, "definition_id"),
                    Text(binding, "provider_id"), OptionalInteger(binding, "max_stage")));
            }
        }
        else
        {
            Properties(root, new[] { "format_version", "id", "stages" }, "localization_key");
            Version(root);
            var stageTokens = Array(root, "stages");
            var stages = new List<ModuleBayStage>();
            var totalArea = 0;
            foreach (var token in stageTokens)
            {
                if (stages.Count == ModuleBayDefinition.MaximumStages)
                    throw Error(token, "definition exceeds the maximum stage count of " + ModuleBayDefinition.MaximumStages);
                var area = ReadLayoutArea(Object(token, "stage")["layout"] ?? token);
                totalArea = checked(totalArea + area);
                if (totalArea > ModuleBayDefinition.MaximumTotalArea)
                    throw Error(token, "definition stage area exceeds " + ModuleBayDefinition.MaximumTotalArea);
                try { stages.Add(DecodeStage(token)); }
                catch (Exception error) when (error is ArgumentException || error is OverflowException)
                { throw Error(token, error.Message); }
            }
            try { contribution.Definitions.Add(new ModuleBayDefinition(Text(root, "id"), stages)); }
            catch (Exception error) when (error is ArgumentException || error is OverflowException)
            { throw Error(root, error.Message); }
        }
        return contribution;
    }

    internal static IReadOnlyList<(string Kind, string Path)> DecodeIndex(string json)
    {
        var root = Load(json, "registry index"); RejectCommentsAndTrailing(json);
        Properties(root, new[] { "format_version", "profiles", "bindings" }); Version(root);
        var result = new List<(string, string)>();
        Add("profiles", "definition"); Add("bindings", "bindings");
        if (result.Select(x => x.Item2).Distinct(StringComparer.Ordinal).Count() != result.Count) throw Error(root, "duplicate index path");
        return result;
        void Add(string property, string kind)
        {
            foreach (var token in Array(root, property))
            {
                var entry = Object(token, property + " entry"); Properties(entry, new[] { "path" });
                var path = Text(entry, "path"); ValidatePath(path); result.Add((kind, path));
            }
        }
    }

    internal static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':') ||
            !path.EndsWith(".json", StringComparison.Ordinal) || path.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
            throw new ArgumentException("Expected a normalized relative .json path.", nameof(path));
    }

    private static int ReadLayoutArea(JToken token)
    {
        var layout = Object(token, "layout");
        var type = Text(layout, "type");
        int width;
        int height;
        if (type == "rectangle")
        {
            Properties(layout, new[] { "type", "width", "height" });
            width = Integer(layout, "width");
            height = Integer(layout, "height");
        }
        else if (type == "pattern")
        {
            Properties(layout, new[] { "type", "rows" });
            var rows = Array(layout, "rows");
            height = 0;
            width = -1;
            foreach (var row in rows)
            {
                if (height == ModuleBayLayout.MaximumDimension) throw Error(row, "pattern height exceeds the supported limit");
                if (row.Type != JTokenType.String) throw Error(row, "pattern row must be a string");
                var length = row.Value<string>()!.Length;
                if (width < 0) width = length;
                if (length != width || length == 0) throw Error(row, "pattern rows must have equal non-empty width");
                if (length > ModuleBayLayout.MaximumDimension) throw Error(row, "pattern width exceeds the supported limit");
                height++;
            }
        }
        else throw Error(layout, "layout type must be rectangle or pattern");
        if (width <= 0 || width > ModuleBayLayout.MaximumDimension || height <= 0 || height > ModuleBayLayout.MaximumDimension)
            throw Error(layout, "layout dimensions exceed the supported limits");
        var area = (long)width * height;
        if (area > ModuleBayLayout.MaximumArea) throw Error(layout, "layout area exceeds " + ModuleBayLayout.MaximumArea);
        return (int)area;
    }

    private static ModuleBayStage DecodeStage(JToken token)
    {
        var stage = Object(token, "stage"); Properties(stage, new[] { "level", "key", "layout" }, "localization_key");
        var layout = Object(stage["layout"]!, "layout");
        var type = Text(layout, "type");
        ModuleBayLayout model;
        if (type == "rectangle")
        {
            Properties(layout, new[] { "type", "width", "height" });
            model = ModuleBayLayout.Rectangle(Integer(layout, "width"), Integer(layout, "height"));
        }
        else if (type == "pattern")
        {
            Properties(layout, new[] { "type", "rows" });
            model = ModuleBayLayout.Pattern(Array(layout, "rows").Select((row, index) => row.Type == JTokenType.String ? row.Value<string>()! : throw Error(row, "row must be a string at index " + index)));
        }
        else throw Error(layout, "layout type must be rectangle or pattern");
        var localization = stage["localization_key"] == null ? null : Text(stage, "localization_key");
        return new ModuleBayStage(Integer(stage, "level"), Text(stage, "key"), model, localization);
    }

    private static JObject Load(string json, string source) =>
        LoadValue(json, source) is JObject value ? value : throw new FormatException(source + " root must be an object");

    private static JToken LoadValue(string json, string source)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new FormatException(source + " exceeds the 1 MiB limit.");
        try
        {
            using var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
            var value = JToken.Load(reader, Settings);
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.Comment) throw new FormatException("JSON comments are not allowed.");
                throw new FormatException(source + " contains trailing tokens.");
            }
            return value;
        }
        catch (JsonException error) { throw new FormatException(source + " contains invalid JSON: " + error.Message, error); }
    }
    private static void RejectCommentsAndTrailing(string json)
    {
        using var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
        while (reader.Read()) if (reader.TokenType == JsonToken.Comment) throw new FormatException("JSON comments are not allowed.");
        // JToken.Load already rejects a second root token.
    }
    private static void Version(JObject root) { if (Integer(root, "format_version") != 1) throw Error(root["format_version"]!, "unsupported format_version"); }
    private static void Properties(JObject obj, IEnumerable<string> required, params string[] optional)
    {
        var requiredSet = new HashSet<string>(required, StringComparer.Ordinal);
        var allowed = new HashSet<string>(requiredSet, StringComparer.Ordinal); foreach (var name in optional) allowed.Add(name);
        foreach (var property in obj.Properties()) if (!allowed.Contains(property.Name)) throw Error(property, "unknown property " + property.Name);
        foreach (var name in requiredSet) if (obj.Property(name, StringComparison.Ordinal) == null) throw Error(obj, "missing property " + name);
    }
    private static JObject Object(JToken token, string label) => token as JObject ?? throw Error(token, label + " must be an object");
    private static IEnumerable<JToken> Array(JObject obj, string name) => obj[name] is JArray array ? array : throw Error(obj[name] ?? obj, name + " must be an array");
    private static string Text(JObject obj, string name) => obj[name]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)obj[name]) ? (string)obj[name]! : throw Error(obj[name] ?? obj, name + " must be a non-empty string");
    private static int Integer(JObject obj, string name)
    {
        var value = obj[name];
        if (value?.Type != JTokenType.Integer || !int.TryParse(value.ToString(Formatting.None), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            throw Error(value ?? obj, name + " must be a signed 32-bit integer");
        return number;
    }
    private static int? OptionalInteger(JObject obj, string name) => obj[name] == null || obj[name]!.Type == JTokenType.Null ? null : Integer(obj, name);
    private static FormatException Error(JToken token, string message)
    {
        var line = (token as IJsonLineInfo)?.LineNumber ?? 0;
        return new FormatException(line > 0 ? message + " (line " + line + ")" : message);
    }
}
