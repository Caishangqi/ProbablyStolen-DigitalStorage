using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Items;

/// <summary>
/// What a canonical identity records about its root item: identifier, types, tags and features. Read from the
/// validated identity bytes, so it needs no live game object.
/// </summary>
public sealed class CanonicalItemSummary
{
    private CanonicalItemSummary(string identifier, IReadOnlyList<string> itemTypes,
        IReadOnlyDictionary<string, Tag> tags, IReadOnlyList<Feature> features, string? spriteAtlasPath, string? spritePath)
    {
        SpriteAtlasPath = spriteAtlasPath;
        SpritePath = spritePath;
        Identifier = identifier;
        ItemTypes = itemTypes;
        Tags = tags;
        Features = features;
    }

    /// <summary>Gets the item identifier.</summary>
    public string Identifier { get; }

    /// <summary>Gets the sprite atlas path recorded for the item's state, if any.</summary>
    public string? SpriteAtlasPath { get; }

    /// <summary>Gets the sprite path recorded for the item's state, if any.</summary>
    public string? SpritePath { get; }

    /// <summary>Gets the item types.</summary>
    public IReadOnlyList<string> ItemTypes { get; }

    /// <summary>Gets the recorded tags by key; a tag with only default values is absent.</summary>
    public IReadOnlyDictionary<string, Tag> Tags { get; }

    /// <summary>Gets the item features.</summary>
    public IReadOnlyList<Feature> Features { get; }

    /// <summary>Gets whether a tag is recorded and enabled.</summary>
    public bool IsTag(string key) => Tags.TryGetValue(key, out var tag) && tag.Enabled;

    /// <summary>One recorded tag.</summary>
    public readonly struct Tag
    {
        internal Tag(bool enabled, string? valueString, int valueInt)
        {
            Enabled = enabled;
            ValueString = valueString;
            ValueInt = valueInt;
        }

        /// <summary>Gets whether the tag is enabled.</summary>
        public bool Enabled { get; }

        /// <summary>Gets the string value.</summary>
        public string? ValueString { get; }

        /// <summary>Gets the integer value.</summary>
        public int ValueInt { get; }
    }

    /// <summary>One item feature.</summary>
    public readonly struct Feature
    {
        internal Feature(string identifier, bool exposed, bool disabled)
        {
            Identifier = identifier;
            Exposed = exposed;
            Disabled = disabled;
        }

        /// <summary>Gets the feature identifier.</summary>
        public string Identifier { get; }

        /// <summary>Gets whether the player has exposed the feature's real value.</summary>
        public bool Exposed { get; }

        /// <summary>Gets whether the feature is disabled.</summary>
        public bool Disabled { get; }
    }

    internal sealed class Builder
    {
        internal string Identifier { get; set; } = string.Empty;
        internal List<string> ItemTypes { get; } = new();
        internal Dictionary<string, Tag> Tags { get; } = new(System.StringComparer.Ordinal);
        internal List<Feature> Features { get; } = new();
        internal string? SpriteAtlasPath { get; set; }
        internal string? SpritePath { get; set; }

        internal CanonicalItemSummary Build() =>
            new(Identifier, ItemTypes.AsReadOnly(), Tags, Features.AsReadOnly(), SpriteAtlasPath, SpritePath);
    }
}
