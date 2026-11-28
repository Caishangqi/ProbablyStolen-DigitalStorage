using System;
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

    /// <summary>
    /// Gets a number that changes whenever the kept contents of the cell change or are dropped. A view held across
    /// frames is still the cell's contents only while this number stays the same.
    /// </summary>
    internal static long Version(GameItem cell) => Versions.GetValue(cell, _ => new Counter()).Value;

    /// <summary>Keeps a view's contents as those of the cell's pages as they are now.</summary>
    internal static void Remember(GameItem cell, CellView view)
    {
        // Keeping the same pages again, as every read that found them unchanged does, is not a change.
        var changed = !Entries.TryGetValue(cell, out var previous) || !previous.Footprint.Matches(cell);
        Entries.Remove(cell);
        Entries.Add(cell, new Entry(view.Pages.ToArray(), view.Usage, Footprint.Of(cell)));
        if (changed) Bump(cell);
    }

    /// <summary>
    /// Drops a cell's kept contents; the next read reads every root again, except roots whose type is still kept
    /// (<see cref="StoredRootKeys"/>).
    /// </summary>
    internal static void Forget(GameItem cell)
    {
        Entries.Remove(cell);
        Bump(cell);
    }

    /// <summary>
    /// Drops a cell's kept contents and the kept type of every root on it (<see cref="StoredRootKeys"/>). For a cell
    /// with a root whose live type no longer matches its record: that root changed unnoticed, so no type kept for the
    /// cell can be trusted.
    /// </summary>
    internal static void ForgetWithRoots(GameItem cell)
    {
        StoredRootKeys.ForgetCell(cell);
        Forget(cell);
    }

    private static readonly ConditionalWeakTable<GameItem, Counter> Versions = new();

    private static void Bump(GameItem cell) => Versions.GetValue(cell, _ => new Counter()).Value++;

    private sealed class Counter
    {
        internal long Value;
    }

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
        private readonly int[] _widths;
        private readonly int[] _heights;
        private readonly Mark[][] _roots;
        private readonly bool _hasSchema;
        private readonly int _schema;
        private readonly string? _layout;
        private readonly bool _hasPageCount;
        private readonly int _pageCount;

        private Footprint(GameGridInventory[] pages, int[] widths, int[] heights, Mark[][] roots, bool hasSchema,
            int schema, string? layout, bool hasPageCount, int pageCount)
        {
            _pages = pages;
            _widths = widths;
            _heights = heights;
            _roots = roots;
            _hasSchema = hasSchema;
            _schema = schema;
            _layout = layout;
            _hasPageCount = hasPageCount;
            _pageCount = pageCount;
        }

        internal static Footprint Of(GameItem cell)
        {
            var pages = CellPages.Pages(cell).ToArray();
            if (!CellTags.TryReadLayout(cell, out var layout))
                throw new InvalidOperationException("The cell layout cannot be read for cache tracking.");
            var hasSchema = cell.IsTag(CellTags.SchemaTag);
            var hasPageCount = cell.IsTag(CellTags.PageCountTag);
            return new Footprint(pages, layout!.Pages.Select(page => page.Width).ToArray(),
                layout.Pages.Select(page => page.Height).ToArray(),
                pages.Select(page => page.childItems.Select(Mark.Of).ToArray()).ToArray(), hasSchema,
                hasSchema ? cell.GetTagReadonly(CellTags.SchemaTag).GetInt() : 0,
                cell.IsTag(CellTags.LayoutTag) ? cell.GetTagReadonly(CellTags.LayoutTag).GetString() : null,
                hasPageCount, hasPageCount ? cell.GetTagReadonly(CellTags.PageCountTag).GetInt() : 0);
        }

        internal bool Matches(GameItem cell)
        {
            try
            {
                if (cell.IsTag(CellTags.SchemaTag) != _hasSchema ||
                    (_hasSchema && cell.GetTagReadonly(CellTags.SchemaTag).GetInt() != _schema) ||
                    (cell.IsTag(CellTags.LayoutTag) ? cell.GetTagReadonly(CellTags.LayoutTag).GetString() : null) != _layout ||
                    cell.IsTag(CellTags.PageCountTag) != _hasPageCount ||
                    (_hasPageCount && cell.GetTagReadonly(CellTags.PageCountTag).GetInt() != _pageCount) ||
                    !CellPages.IsComplete(cell)) return false;
                var tabs = CellPages.Tabs(cell);
                if (tabs == null) return false;
                var panel = (cell.contentWindow?.childElement as MachineryPanelRow)?.GetPanel(CellPages.PanelRole);
                if (panel == null || !ReferenceEquals(tabs.owner, panel.owner) ||
                    !((PixelElement)tabs).IsRenderingChildOf(panel)) return false;
                var pages = CellPages.Pages(cell);
                if (pages.Count != _pages.Length) return false;
                for (var index = 0; index < pages.Count; index++)
                {
                    if (!ReferenceEquals(pages[index], _pages[index]) ||
                        !ReferenceEquals(pages[index].GetParentItem(), cell) ||
                        pages[index].inventoryShape.width != _widths[index] ||
                        pages[index].inventoryShape.height != _heights[index] ||
                        !pages[index].parents.Contains(panel.owner)) return false;
                    var items = pages[index].childItems;
                    var marks = _roots[index];
                    if (items.Count != marks.Length) return false;
                    for (var root = 0; root < marks.Length; root++)
                        if (!marks[root].Matches(items[root], pages[index])) return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
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

        internal bool Matches(GameItem item, GameGridInventory owner) =>
            ReferenceEquals(item, _item) && ReferenceEquals(item.parentInventory, owner) && item.unitCount == _units &&
            item.modifiedShape.minX == _x && item.modifiedShape.minY == _y && CellPages.SizeOf(item).Equals(_size) &&
            !(item is GameItemElement element && element.IsDestroyed());
    }
}
