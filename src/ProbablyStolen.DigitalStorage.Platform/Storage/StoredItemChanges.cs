using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Identity;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Notices native changes to the features, tags and lore of stored items. Stored items should keep the state they
/// were stored with, but game code and other mods can still change them, for example whenever an item's price is read.
/// <list type="bullet">
/// <item>A change made while this mod reads a stored item (<see cref="Read"/>) is undone when the read ends.</item>
/// <item>Any other change has its stored root checked on a later main-thread turn (<see cref="StoredRootChecks{TCell,TRoot}"/>);
/// a root whose type no longer matches its recorded slot makes its cell read again.</item>
/// </list>
/// Changes this mod's own moves cause are left to the move, which confirms the moved root itself.
/// </summary>
public sealed class StoredItemChanges
{
    private static StoredItemChanges? _active;
    private static ReadScope? _scope;
    private static int _quiet;
    private readonly Func<GameItem, (GameItem Cell, GameItem Root)?> _locate;
    private readonly Func<bool> _paused;
    private readonly StoredRootChecks<GameItem, GameItem> _checks;
    private bool _restoring;

    /// <param name="locate">Finds the cell and stored root an item belongs to, or null for an item not in storage.</param>
    /// <param name="paused">Whether changes are ignored, such as while a save decodes.</param>
    /// <param name="checks">Checks the roots of changes made outside a read.</param>
    internal StoredItemChanges(Func<GameItem, (GameItem Cell, GameItem Root)?> locate, Func<bool> paused,
        StoredRootChecks<GameItem, GameItem> checks)
    {
        _locate = locate ?? throw new ArgumentNullException(nameof(locate));
        _paused = paused ?? throw new ArgumentNullException(nameof(paused));
        _checks = checks ?? throw new ArgumentNullException(nameof(checks));
    }

    /// <summary>Starts or, with null, stops noticing changes.</summary>
    internal static void Activate(StoredItemChanges? changes) => _active = changes;

    /// <summary>
    /// Undoes, when disposed, the changes made to the stored root of <paramref name="item"/> and the items inside it.
    /// Features and lore are restored; a changed tag cannot be, so the root is checked instead. Does nothing for an item
    /// that is not stored or already being read.
    /// </summary>
    internal static IDisposable Read(GameItem? item)
    {
        if (item == null || _active is not { } active || active._paused()) return ReadScope.None;
        if (active._locate(item) is not { } located || Covering(located.Root) != null) return ReadScope.None;
        var scope = new ReadScope(active, located.Cell, located.Root, _scope);
        scope.Keep(located.Root);
        _scope = scope;
        return scope;
    }

    /// <summary>Ignores changes until disposed; used around this mod's own native moves.</summary>
    internal static Quiet Silence()
    {
        _quiet++;
        return new Quiet();
    }

    /// <summary>Called before a native member may change an item's features or lore.</summary>
    public static void Changing(GameItem? item)
    {
        if (_scope == null || !Watching(item, out var active)) return;
        if (active._locate(item!) is { } located) Covering(located.Root)?.Keep(item!);
    }

    /// <summary>Called after a native member changed an item.</summary>
    /// <param name="item">The changed item.</param>
    /// <param name="restorable">Whether a read can undo the change (features and lore, not tags).</param>
    public static void Changed(GameItem? item, bool restorable)
    {
        if (!Watching(item, out var active) || active._locate(item!) is not { } located) return;
        if (Covering(located.Root) is { } scope)
        {
            if (!restorable) scope.Unrestorable = true;
            return;
        }

        active._checks.Mark(located.Cell, located.Root);
    }

    /// <summary>
    /// Finds the cell and stored root of an item: the item itself when it lies on a cell page, otherwise the nearest
    /// item holding it that does.
    /// </summary>
    internal static (GameItem Cell, GameItem Root)? Locate(GameItem item)
    {
        var current = item;
        for (var depth = 0; current?.parentInventory is { } inventory && depth <= 16; depth++)
        {
            if (CellPages.CellOfStored(current, out _) is { } cell) return (cell, current);
            current = GraphUtils.FindParentType<GameItem>(inventory);
        }

        return null;
    }

    /// <summary>
    /// Whether a stored root still has the type its cell's kept contents record. A cell with nothing kept, or a root
    /// that has left the cell, has nothing to go stale: the next read reads it again anyway.
    /// </summary>
    internal static bool StillMatches(GameItem cell, GameItem root)
    {
        if (!CellInventoryCache.TryGet(cell, out var pages, out _)) return true;
        if (!ReferenceEquals(CellPages.CellOfStored(root, out var page), cell)) return true;
        if (page < 0 || page >= pages.Count || pages[page].Kind != PageKind.Strip) return false;
        var slot = StripLayout.SlotIndexAt(pages[page].Geometry, root.modifiedShape.minX, root.modifiedShape.minY);
        return slot >= 0 && pages[page].KeyAt(slot) is { } recorded && recorded.Equals(StorageService.Identify(root));
    }

    private static bool Watching(GameItem? item, out StoredItemChanges active)
    {
        active = _active!;
        return item != null && active != null && _quiet == 0 && !active._restoring && !active._paused();
    }

    private static ReadScope? Covering(GameItem root)
    {
        for (var scope = _scope; scope != null; scope = scope.Parent)
            if (ReferenceEquals(scope.Root, root)) return scope;
        return null;
    }

    internal readonly struct Quiet : IDisposable
    {
        public void Dispose() => _quiet--;
    }

    private sealed class ReadScope : IDisposable
    {
        internal static readonly IDisposable None = new Nothing();
        private readonly StoredItemChanges? _owner;
        private readonly GameItem? _cell;
        private readonly Dictionary<GameItem, ItemState> _kept = new(Reference.Comparer);
        private bool _disposed;

        internal ReadScope(StoredItemChanges owner, GameItem cell, GameItem root, ReadScope? parent)
        {
            _owner = owner;
            _cell = cell;
            Root = root;
            Parent = parent;
        }

        internal GameItem Root { get; }

        internal ReadScope? Parent { get; }

        internal bool Unrestorable { get; set; }

        internal void Keep(GameItem item)
        {
            if (!_kept.ContainsKey(item)) _kept[item] = ItemState.Of(item);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scope = Parent;
            var restored = true;
            _owner!._restoring = true;
            try
            {
                foreach (var pair in _kept)
                {
                    try
                    {
                        pair.Value.Restore(pair.Key, VolatileFeatureRules.Current);
                    }
                    catch (Exception)
                    {
                        restored = false;
                    }
                }
            }
            finally
            {
                _owner._restoring = false;
            }

            if ((Unrestorable || !restored) && _active == _owner) _owner._checks.Mark(_cell!, Root);
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class ItemState
    {
        private readonly string _description;
        private readonly string _flavor;
        private readonly ItemFeature[]? _features;
        private readonly int[]? _owners;

        private ItemState(string description, string flavor, ItemFeature[]? features, int[]? owners)
        {
            _description = description;
            _flavor = flavor;
            _features = features;
            _owners = owners;
        }

        internal static ItemState Of(GameItem item)
        {
            var features = item.itemFeatures?.ToArray();
            int[]? owners = null;
            if (features != null)
            {
                owners = new int[features.Length];
                for (var index = 0; index < features.Length; index++) owners[index] = features[index].parentItemUniqueId;
            }

            return new ItemState(item.shortDescription, item.flavorText, features, owners);
        }

        // Features go back into the list directly: adding them through the game would run their activation actions.
        // Volatile features are not part of the type, so they stay as the read left them: their owner may rewrite
        // them only now and then, and undoing them would make it show them only every so often.
        internal void Restore(GameItem item, VolatileFeatureRules rules)
        {
            if (!string.Equals(item.shortDescription, _description, StringComparison.Ordinal)) item.shortDescription = _description;
            if (!string.Equals(item.flavorText, _flavor, StringComparison.Ordinal)) item.flavorText = _flavor;
            var list = item.itemFeatures;
            if (list == null || _features == null || SameKept(list, _features, rules)) return;
            var restored = new List<ItemFeature>(_features.Length);
            for (var index = 0; index < _features.Length; index++)
            {
                if (rules.IsVolatile(_features[index].identifier)) continue;
                _features[index].parentItemUniqueId = _owners![index];
                restored.Add(_features[index]);
            }

            foreach (var feature in list)
            {
                if (rules.IsVolatile(feature.identifier)) restored.Add(feature);
                else if (Array.IndexOf(_features, feature) < 0) feature.parentItemUniqueId = -1;
            }

            list.Clear();
            list.AddRange(restored);
        }

        private static bool SameKept(List<ItemFeature> list, ItemFeature[] features, VolatileFeatureRules rules)
        {
            var index = 0;
            foreach (var feature in list)
            {
                if (rules.IsVolatile(feature.identifier)) continue;
                while (index < features.Length && rules.IsVolatile(features[index].identifier)) index++;
                if (index == features.Length || !ReferenceEquals(feature, features[index])) return false;
                index++;
            }

            while (index < features.Length && rules.IsVolatile(features[index].identifier)) index++;
            return index == features.Length;
        }
    }

    private sealed class Reference : IEqualityComparer<GameItem>
    {
        internal static readonly Reference Comparer = new();

        public bool Equals(GameItem? left, GameItem? right) => ReferenceEquals(left, right);

        public int GetHashCode(GameItem value) => RuntimeHelpers.GetHashCode(value);
    }
}
