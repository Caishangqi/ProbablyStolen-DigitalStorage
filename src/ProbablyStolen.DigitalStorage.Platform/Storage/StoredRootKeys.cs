using System;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Platform.Identity;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Keeps each stored root's type while it stays on its page, so reading a cell's pages again (after compaction, an
/// unaccounted move or any other reason the cell's kept contents were dropped) reads only roots that are new or
/// changed. Only cell page reads use it; every check before or after a move reads the live item.
/// <para>
/// A kept type is used only while the root is still on the page it was read on and a few cheap fields (identifier,
/// feature count, descriptions) are unchanged. It is dropped when the root leaves a page, when a change to it is
/// noticed (<see cref="StoredItemChanges"/>), and for every root of a cell whose live type did not match its record.
/// </para>
/// </summary>
internal static class StoredRootKeys
{
    private static ConditionalWeakTable<GameItem, Entry> _entries = new();

    /// <summary>Gets a stored root's type, from what was kept when it still applies.</summary>
    internal static ItemKey? Identify(GameItem root)
    {
        if (root.parentInventory is not GameGridInventory page) return StorageService.Identify(root);
        if (_entries.TryGetValue(root, out var entry) && entry.Matches(root, page))
        {
            TransferProfile.Add(TransferPart.IdentifyKept, TransferProfile.Mark());
            return entry.Key;
        }

        var key = StorageService.Identify(root, out var failure);
        if (key != null && failure == NativeItemIdentityFailure.None) Keep(root, page, key);
        else _entries.Remove(root);
        return key;
    }

    /// <summary>Keeps the type of a root this mod just stored and confirmed.</summary>
    internal static void Remember(GameItem root, ItemKey key)
    {
        if (root?.parentInventory is GameGridInventory page && key != null) Keep(root, page, key);
    }

    /// <summary>Drops one root's kept type.</summary>
    internal static void Forget(GameItem? root)
    {
        if (root != null) _entries.Remove(root);
    }

    /// <summary>Drops the kept type of every root on a cell's pages.</summary>
    internal static void ForgetCell(GameItem cell)
    {
        try
        {
            foreach (var page in CellPages.Pages(cell))
                foreach (var root in page.childItems)
                    Forget(root);
        }
        catch (Exception)
        {
            // Pages that cannot be walked cannot say which roots were theirs.
            Clear();
        }
    }

    /// <summary>Drops every kept type.</summary>
    internal static void Clear() => _entries = new ConditionalWeakTable<GameItem, Entry>();

    private static void Keep(GameItem root, GameGridInventory page, ItemKey key)
    {
        _entries.Remove(root);
        _entries.Add(root, new Entry(key, page, root));
    }

    private sealed class Entry
    {
        private readonly GameGridInventory _page;
        private readonly string _identifier;
        private readonly int _features;
        private readonly string _description;
        private readonly string _flavor;

        internal Entry(ItemKey key, GameGridInventory page, GameItem root)
        {
            Key = key;
            _page = page;
            _identifier = root.identifier;
            _features = root.itemFeatures?.Count ?? -1;
            _description = root.shortDescription;
            _flavor = root.flavorText;
        }

        internal ItemKey Key { get; }

        // Catches some changes no patch reports, such as writing the feature list directly; the rest are caught by
        // the check of every root about to leave.
        internal bool Matches(GameItem root, GameGridInventory page) =>
            ReferenceEquals(page, _page) &&
            string.Equals(root.identifier, _identifier, StringComparison.Ordinal) &&
            (root.itemFeatures?.Count ?? -1) == _features &&
            string.Equals(root.shortDescription, _description, StringComparison.Ordinal) &&
            string.Equals(root.flavorText, _flavor, StringComparison.Ordinal);
    }
}
