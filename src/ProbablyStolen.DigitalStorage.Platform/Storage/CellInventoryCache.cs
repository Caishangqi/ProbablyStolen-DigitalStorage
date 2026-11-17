using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Keeps each cell's page contents (the type in every slot and the usage per type) between operations, so a cell is
/// read root by root only once instead of on every store, extract or terminal refresh. Reading a root's type walks
/// and encodes the whole item, so reading every root on each operation would make it scale with everything stored.
/// <para>
/// The pages stay the only record. Kept contents are used only while the pages hold exactly the roots they held when
/// the contents were kept: the same page objects, and on each page the same items in the same order, with the same
/// unit counts, positions and sizes. Any change made some other way therefore makes the next read start over. This
/// mod's own verified moves update the contents in place, and <see cref="Remember"/> then marks them current. A root
/// keeps its type while stored: stored items are out of the game's nightly cycle (<see cref="StoredItemPresence"/>),
/// and every root about to leave is checked against its recorded type.
/// </para>
/// </summary>
internal static class CellInventoryCache
{
    private static readonly ConditionalWeakTable<GameItem, Entry> Entries = new();

    /// <summary>Gets the kept contents of a cell when its pages have not changed since they were kept.</summary>
    internal static bool TryGet(GameItem cell, out IReadOnlyList<PageView> pages, out CellUsage usage)
    {
        pages = System.Array.Empty<PageView>();
        usage = null!;
        if (!Entries.TryGetValue(cell, out var entry) || !entry.Footprint.Matches(cell)) return false;
        pages = entry.Pages;
        usage = entry.Usage;
        return true;
    }

    /// <summary>Keeps a view's contents as those of the cell's pages as they are now.</summary>
    internal static void Remember(GameItem cell, CellView view)
    {
        Entries.Remove(cell);
        Entries.Add(cell, new Entry(view.Pages.ToArray(), view.Usage, Footprint.Of(cell)));
    }

    /// <summary>Drops a cell's kept contents; the next read reads every root again.</summary>
    internal static void Forget(GameItem cell) => Entries.Remove(cell);

    private sealed class Entry
    {
        internal Entry(IReadOnlyList<PageView> pages, CellUsage usage, Footprint footprint)
        {
            Pages = pages;
            Usage = usage;
            Footprint = footprint;
        }

        internal IReadOnlyList<PageView> Pages { get; }

        internal CellUsage Usage { get; }

        internal Footprint Footprint { get; }
    }

    // What the pages held, without reading any root's type: cheap enough to compare on every read.
    private sealed class Footprint
    {
        private readonly GameGridInventory[] _pages;
        private readonly Mark[][] _roots;

        private Footprint(GameGridInventory[] pages, Mark[][] roots)
        {
            _pages = pages;
            _roots = roots;
        }

        internal static Footprint Of(GameItem cell)
        {
            var pages = CellPages.Pages(cell).ToArray();
            return new Footprint(pages, pages.Select(page => page.childItems.Select(Mark.Of).ToArray()).ToArray());
        }

        internal bool Matches(GameItem cell)
        {
            var pages = CellPages.Pages(cell);
            if (pages.Count != _pages.Length) return false;
            for (var index = 0; index < pages.Count; index++)
            {
                if (!ReferenceEquals(pages[index], _pages[index])) return false;
                var items = pages[index].childItems;
                var marks = _roots[index];
                if (items.Count != marks.Length) return false;
                for (var root = 0; root < marks.Length; root++)
                {
                    if (!marks[root].Matches(items[root])) return false;
                }
            }

            return true;
        }
    }

    private readonly struct Mark
    {
        private readonly GameItem _item;
        private readonly int _units;
        private readonly int _x;
        private readonly int _y;
        private readonly BoundingSize _size;

        private Mark(GameItem item, int units, int x, int y, BoundingSize size)
        {
            _item = item;
            _units = units;
            _x = x;
            _y = y;
            _size = size;
        }

        internal static Mark Of(GameItem item) =>
            new(item, item.unitCount, item.modifiedShape.minX, item.modifiedShape.minY, CellPages.SizeOf(item));

        internal bool Matches(GameItem item) =>
            ReferenceEquals(item, _item) && item.unitCount == _units && item.modifiedShape.minX == _x &&
            item.modifiedShape.minY == _y && CellPages.SizeOf(item).Equals(_size) &&
            !(item is GameItemElement element && element.IsDestroyed());
    }
}
