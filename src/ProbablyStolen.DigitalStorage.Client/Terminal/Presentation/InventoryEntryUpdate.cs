using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>One serial-addressed update for a key in the terminal's known network inventory.</summary>
public sealed class InventoryEntryUpdate
{
    /// <summary>Creates a complete entry or an incremental update for a previous serial.</summary>
    public InventoryEntryUpdate(long serial, ItemKey? what, long storedAmount, long requestableAmount = 0,
        bool craftable = false, string? displayName = null, string? spriteKey = null, int spriteWidth = 1,
        int spriteHeight = 1, SearchDocument? searchDocument = null)
    {
        Serial = serial;
        What = what;
        StoredAmount = storedAmount;
        RequestableAmount = requestableAmount;
        Craftable = craftable;
        DisplayName = displayName;
        SpriteKey = spriteKey;
        SpriteWidth = spriteWidth;
        SpriteHeight = spriteHeight;
        SearchDocument = searchDocument;
    }

    /// <summary>Gets the stable identifier for this key during the menu session.</summary>
    public long Serial { get; }

    /// <summary>Gets the key on complete updates, or null when metadata can be reused by serial.</summary>
    public ItemKey? What { get; }

    /// <summary>Gets the quantity currently stored under the key.</summary>
    public long StoredAmount { get; }

    /// <summary>Gets the quantity requestable from attached external networks.</summary>
    public long RequestableAmount { get; }

    /// <summary>Gets whether the key is craftable. DigitalStorage currently reports false.</summary>
    public bool Craftable { get; }

    /// <summary>Gets an optional localized display name.</summary>
    public string? DisplayName { get; }

    /// <summary>Gets an optional runtime sprite lookup key.</summary>
    public string? SpriteKey { get; }

    /// <summary>Gets the source sprite width.</summary>
    public int SpriteWidth { get; }

    /// <summary>Gets the source sprite height.</summary>
    public int SpriteHeight { get; }

    /// <summary>Gets detached searchable metadata captured with the complete update.</summary>
    public SearchDocument? SearchDocument { get; }

    /// <summary>Returns whether this entry represents stored, requestable or craftable content.</summary>
    public bool IsMeaningful() => StoredAmount > 0 || RequestableAmount > 0 || Craftable;

    internal TerminalEntry ToTerminalEntry()
    {
        if (What is not ItemKey itemKey ||
            string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(SpriteKey))
            throw new InvalidOperationException("The terminal repository entry is incomplete for display.");
        return new TerminalEntry(itemKey, StoredAmount, DisplayName, SpriteKey, SpriteWidth, SpriteHeight, Serial, SearchDocument);
    }
}
