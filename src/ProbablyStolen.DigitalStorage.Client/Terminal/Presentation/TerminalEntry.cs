using System;
using System.Text;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Immutable display data for one stored item; it never owns a physical item.</summary>
public sealed class TerminalEntry
{
    /// <summary>Creates a display entry or a zero-quantity placeholder retained during paused reordering.</summary>
    public TerminalEntry(ItemKey key, long amount, string displayName, string spriteKey,
        int spriteWidth = 1, int spriteHeight = 1, long serial = -1, SearchDocument? document = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A display name is required.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(spriteKey))
        {
            throw new ArgumentException("A sprite key is required.", nameof(spriteKey));
        }

        if (spriteWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spriteWidth));
        }

        if (spriteHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spriteHeight));
        }

        Serial = serial;
        Amount = amount;
        DisplayName = displayName;
        SpriteKey = spriteKey;
        SpriteWidth = spriteWidth;
        SpriteHeight = spriteHeight;
        Document = document ?? new SearchDocument(displayName, key.Identifier);
    }

    /// <summary>Gets the menu serial captured by this view entry, or -1 before menu synchronization.</summary>
    public long Serial { get; }

    /// <summary>Gets the authoritative storage identity.</summary>
    public ItemKey Key { get; }

    /// <summary>Gets the available quantity, or zero for a temporarily retained view placeholder.</summary>
    public long Amount { get; }

    /// <summary>Gets the localized display name supplied by the platform.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the runtime sprite key supplied by the platform.</summary>
    public string SpriteKey { get; }

    /// <summary>Gets the source sprite width in pixels.</summary>
    public int SpriteWidth { get; }

    /// <summary>Gets the source sprite height in pixels.</summary>
    public int SpriteHeight { get; }

    /// <summary>Gets detached searchable metadata captured with the terminal snapshot.</summary>
    public SearchDocument Document { get; }

    /// <summary>Gets a sort key that follows identity order when compared using ordinal string comparison.</summary>
    public string GetIdentitySortKey() => BuildIdentitySortKey(Key);

    internal static string BuildIdentitySortKey(ItemKey key)
    {
        var builder = new StringBuilder();
        AppendOrdinalString(builder, key.Identifier);
        builder.Append(key.IsCanonical ? '\u0001' : '\0');
        if (key.IsCanonical)
        {
            foreach (var value in key.CopyCanonicalState())
            {
                builder.Append('\u0001').Append((char)value);
            }

            builder.Append('\0');
            return builder.ToString();
        }

        foreach (var component in key.Components)
        {
            builder.Append('\u0001');
            AppendOrdinalString(builder, component.Key);
            AppendOrdinalString(builder, component.Value);
        }

        builder.Append('\0');
        return builder.ToString();
    }

    private static void AppendOrdinalString(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
            builder.Append('\u0001').Append(character);
        }

        builder.Append('\0');
    }

    /// <summary>Checks the detached document against the configured terminal search service.</summary>
    public bool Matches(TerminalSearchService service, string query)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));
        return service.Matches(Document, query);
    }
}
