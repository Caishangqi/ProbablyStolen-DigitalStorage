using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Native page operations on one storage cell item. Pages are locked <see cref="GameGridInventory"/>s inside a
/// <see cref="MachineryTabs"/> on the cell's pages panel (found by role, before the module bay in save order), so the
/// native save graph stores their items.
/// Page sizes follow the cell's layout descriptor: page 0 is the empty 12x12 anchor, schema 1 pages are 12x12, and
/// strip pages are one row of N slots. Items move with the native placement sequence (Expel, then set
/// <c>modifiedShape</c>, then UncheckedAccept, as in <c>ItemMultiSelectHandler.CommitPrecisePlacement</c>); those calls
/// bypass the page locks.
/// </summary>
internal static class CellPages
{
    /// <summary>
    /// The pages panel role. It must not be <c>"storage"</c>: the native <c>StorageExpansionHelper.InitTabNames</c>
    /// looks that role up on every decoded item and would take over the tab captions.
    /// </summary>
    internal const string PanelRole = "digital_storage.pages";

    private static readonly ConditionalWeakTable<GameItem, PageSetSwap<MachineryTabs>> Swaps = new();

    /// <summary>Creates the pages panel with its anchor page.</summary>
    internal static MachineryPanel CreatePanel(string label)
    {
        var tabs = new MachineryTabs();
        AddPage(tabs, PageGeometry.Anchor);
        return MachineHelper.Panel(label, tabs, PanelRole);
    }

    /// <summary>Gets the page tabs of a cell, or null when the item has no pages panel.</summary>
    internal static MachineryTabs? Tabs(GameItem cell) =>
        (cell.contentWindow?.childElement as MachineryPanelRow)?.GetPanel(PanelRole)?.content as MachineryTabs;

    /// <summary>Gets the pages in page order.</summary>
    internal static IReadOnlyList<GameGridInventory> Pages(GameItem cell)
    {
        var tabs = Tabs(cell);
        return tabs == null ? Array.Empty<GameGridInventory>() : Pages(tabs);
    }

    private static IReadOnlyList<GameGridInventory> Pages(MachineryTabs tabs)
    {
        var pages = new List<GameGridInventory>(tabs.count);
        for (var index = 0; index < tabs.count; index++)
        {
            if (tabs.GetTabInventory(index) is GameGridInventory page) pages.Add(page);
        }

        return pages;
    }

    /// <summary>Gets the session page swap of a cell; it keeps an interrupted swap's containers until it finishes.</summary>
    internal static PageSetSwap<MachineryTabs> SwapOf(GameItem cell) => Swaps.GetValue(cell, _ => new PageSetSwap<MachineryTabs>());

    /// <summary>Gets whether the cell's pages match its recorded layout.</summary>
    internal static bool IsComplete(GameItem cell)
    {
        var tabs = Tabs(cell);
        return tabs != null && CellTags.TryReadLayout(cell, out var layout) && IsLinked(cell, tabs, layout!);
    }

    /// <summary>Gets whether the pages are attached, linked to the cell and sized exactly as the layout says.</summary>
    internal static bool IsLinked(GameItem cell, MachineryTabs tabs, LayoutDescriptor layout)
    {
        var row = cell.contentWindow?.childElement as MachineryPanelRow;
        var panel = row?.GetPanel(PanelRole);
        var count = layout.Pages.Count;
        if (SaveManager.IsDecoding || panel?.owner == null || !ReferenceEquals(panel.content, tabs) ||
            !((PixelElement)tabs).IsRenderingChildOf(panel) || !ReferenceEquals(tabs.owner, panel.owner) ||
            tabs.count != count) return false;
        // The native children getter collects every panel once; GetBodyStorage would recollect for every page.
        // Save order is component slot, pages, then the module bay (CellContentWindow), so the bay is last.
        var storage = row!.children;
        if (storage.Count != count + 2 || storage[0] is not GameSlotInventory slot ||
            slot.identifier != Client.CellContentWindow.SlotId || !slot.parents.Contains(panel.owner) ||
            storage[count + 1] is not GameGridInventory { identifier: CellUpgradeInventory.BayId }) return false;
        for (var index = 0; index < count; index++)
        {
            var geometry = layout.Pages[index];
            if (tabs.GetTabInventory(index) is not GameGridInventory page || !ReferenceEquals(storage[index + 1], page) ||
                page.inventoryShape.width != geometry.Width || page.inventoryShape.height != geometry.Height ||
                !page.parents.Contains(panel.owner)) return false;
        }

        return true;
    }

    /// <summary>Gets whether no page holds an item.</summary>
    internal static bool IsEmpty(GameItem cell) => Pages(cell).All(page => page.childItems.Count == 0);

    /// <summary>Gets every item root on every page.</summary>
    internal static IEnumerable<GameItem> Roots(GameItem cell) => Pages(cell).SelectMany(page => page.childItems);

    /// <summary>
    /// Gets the cell whose page directly holds <paramref name="root"/>, and the page index, or null when the root is
    /// not on a storage cell page.
    /// </summary>
    internal static GameItem? CellOfStored(GameItem? root, out int pageIndex)
    {
        pageIndex = -1;
        if (root?.parentInventory is not GameGridInventory page || !page.overrideLockInsert) return null;
        var cell = page.GetParentItem();
        if (!BasicCellHandler.IsCell(cell)) return null;
        var pages = Pages(cell!);
        for (var index = 0; index < pages.Count; index++)
        {
            if (ReferenceEquals(pages[index], page))
            {
                pageIndex = index;
                return cell;
            }
        }

        return null;
    }

    /// <summary>
    /// Appends the recorded pages native decoding has not rebuilt yet. Used only by the decode postfix; it never
    /// writes tags, so a cell whose layout cannot be restored keeps its tags for the audit to report.
    /// </summary>
    internal static void RestorePages(GameItem cell)
    {
        var tabs = Tabs(cell);
        if (tabs == null) return;
        var layout = CellTags.TryReadLayout(cell, out var read) ? read : null;
        var missing = CellRecord.RestorePlan(CellTags.ReadSchema(cell), layout, tabs.count)
            ?? throw new InvalidOperationException("The recorded cell layout cannot be restored.");
        foreach (var geometry in missing) AddPage(tabs, geometry);
        if (missing.Count > 0) Refresh(cell);
    }

    /// <summary>Gets an item's bounding size in its current orientation.</summary>
    internal static BoundingSize SizeOf(GameItem item)
    {
        var shape = item.modifiedShape;
        return new BoundingSize(shape.globalWidth, shape.globalHeight);
    }

    /// <summary>
    /// Builds the view for one operation from the cell's live pages.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The layout cannot be read, the pages do not match it, a strip root is off its slot grid, or a root has no
    /// type. The cell is then treated as unavailable, never as empty.
    /// </exception>
    internal static CellView Read(GameItem cell, Func<GameItem, ItemKey?> identify, CellWriteState writeState)
    {
        var tier = StorageCellTier.FromCellId(cell.identifier) ?? throw new InvalidOperationException("Unknown storage cell tier.");
        if (!CellTags.TryReadLayout(cell, out var layout)) throw new InvalidOperationException("The cell layout cannot be read.");
        var pages = Pages(cell);
        if (pages.Count != layout!.Pages.Count) throw new InvalidOperationException("The cell pages do not match its layout.");

        var usage = new CellUsage();
        var views = new List<PageView>(pages.Count);
        for (var index = 0; index < pages.Count; index++)
        {
            var geometry = layout.Pages[index];
            var slots = new List<int>();
            foreach (var root in pages[index].childItems)
            {
                var size = SizeOf(root);
                if (geometry.Kind == PageKind.Strip)
                {
                    var slot = StripLayout.SlotIndexAt(geometry, root.modifiedShape.minX, root.modifiedShape.minY);
                    if (!size.Equals(geometry.SlotSize) || slot < 0)
                        throw new InvalidOperationException($"A root on strip page {index} is off its slot grid.");
                    slots.Add(slot);
                }

                var key = identify(root) ?? throw new InvalidOperationException($"A root on page {index} has no type.");
                usage.Add(key, root.unitCount, CellUsage.AreaOf(size, root.unitCount));
            }

            views.Add(geometry.Kind == PageKind.Strip
                ? PageView.Strip(geometry, slots)
                : PageView.Unslotted(geometry, pages[index].childItems.Count));
        }

        var typeLimit = CellTypeLimitSource.EffectiveOrBase(cell);
        return new CellView(CellRules.For(tier), typeLimit, views, usage, CellPartitionTags.Read(cell),
            DrivePriorityTag.ReadForCell(cell), writeState, CellUpgradeInventory.Read(cell));
    }

    /// <summary>
    /// Appends one strip page, then records it in the layout and verifies. On failure the page is removed again and
    /// the previous layout restored. Appending as the last tab keeps every other tab's index unchanged.
    /// </summary>
    internal static bool TryAppendStripPage(GameItem cell, PageGeometry geometry)
    {
        var tabs = Tabs(cell);
        if (tabs == null || !CellTags.TryReadLayout(cell, out var before) || before!.Pages.Count >= LayoutDescriptor.MaxPages ||
            !IsLinked(cell, tabs, before)) return false;

        var after = before.WithAppended(geometry);
        AddPage(tabs, geometry);
        CellTags.WriteLayout(cell, after);
        Refresh(cell);
        if (IsLinked(cell, tabs, after)) return true;

        var element = tabs.GetPage(tabs.count - 1);
        if (element != null && tabs.Detach(element)) UnityEngine.Object.Destroy(element.handler.gameObject);
        CellTags.WriteLayout(cell, before);
        Refresh(cell);
        return false;
    }

    /// <summary>
    /// Removes empty pages after items left the cell; does nothing while compaction is blocked. Failures never
    /// propagate: they are recorded in <see cref="CellHealth"/> (blocked after a rollback, unavailable when the
    /// rollback failed too) and logged with the exception type only.
    /// </summary>
    internal static void Compact(GameItem cell)
    {
        var swap = SwapOf(cell);
        var status = CellHealth.Get(cell).Status;
        // Cells restricted to extraction keep their schema 1 layout until a later load migrates them.
        if (swap.Blocked || status != CellHealthStatus.Available) return;
        try
        {
            var port = new NativeSwapPort(cell);
            if (!port.CanOperate || port.RecordedLayout is not { } layout) return;
            var plan = PageCompactionPlanner.Plan(layout, port.RootCounts());
            if (!plan.HasWork && !swap.Pending) return;
            Swap(cell, swap, port, plan);
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogWarning($"Digital Storage: empty page removal for {cell.identifier} failed " +
                $"({error.GetType().Name}); health={CellHealth.Get(cell).Status}.");
        }
    }

    /// <summary>Removes every page but the anchor from an empty housing.</summary>
    internal static void ResetToAnchor(GameItem cell)
    {
        var port = new NativeSwapPort(cell, housing: true);
        var layout = port.RecordedLayout ?? throw new InvalidOperationException("The housing layout cannot be read.");
        var counts = port.RootCounts();
        if (counts.Any(count => count != 0)) throw new InvalidOperationException("The housing pages are not empty.");
        var swap = SwapOf(cell);
        swap.Request();
        swap.Run(port, PageCompactionPlanner.Plan(layout, counts));
    }

    /// <summary>Gets whether a housing is idle at exactly its empty anchor page, so it may be reassembled.</summary>
    internal static bool IsAtAnchor(GameItem cell) =>
        Tabs(cell) != null && SwapOf(cell).IsAtAnchor(new NativeSwapPort(cell, housing: true));

    /// <summary>Runs a planned swap and records the outcome in the cell health.</summary>
    internal static void Swap(GameItem cell, PageSetSwap<MachineryTabs> swap, IPageSetSwapPort<MachineryTabs> port, PageCompaction plan)
    {
        try
        {
            swap.Run(port, plan);
        }
        catch (AggregateException)
        {
            CellHealth.Set(cell, new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.RollbackFailed));
            CellHealth.WarnDoNotSaveDuringPlay("page cleanup and its rollback failed");
            throw;
        }
        catch (Exception) when (swap.Blocked)
        {
            // The swap flag blocks compaction without changing an existing extraction restriction.
            if (CellHealth.Get(cell).Status == CellHealthStatus.Available)
                CellHealth.Set(cell, new CellHealthState(CellHealthStatus.CompactionBlocked));
            throw;
        }
    }

    internal static GameGridInventory AddPage(MachineryTabs tabs, PageGeometry geometry)
    {
        var page = new GameGridInventory(geometry.Width, geometry.Height).SetMachineryStyle();
        // Players reach page items only through the terminal: no drag in or out, and no "use" from the cell window.
        // Insertion uses the native lock because it is checked before HopperHelper.MayBypassGridRules; removal uses
        // callbacks instead of the native lock, because a page locked both ways makes every item tooltip show
        // "locked, machine running" (GameItemElement.GetTooltipBasic). Returning false for the inventory vetoes activation
        // even when the item allows it (GameItem.MayActivateSlotItem). Expel/UncheckedAccept ignore all of these.
        // Removal is refused unless a public API external access scope is open and the cell is online; even then the
        // caller moves roots through the API, the callbacks only let another mod's scan see them.
        page.overrideLockInsert = true;
        page.mayInventoryRemoveItemFunc = (item, _) => ExternalAccessScope.AllowsRemoval(item);
        page.maxSlotRemoveItemFunc = (item, _, _) => ExternalAccessScope.AllowsRemoval(item) ? item.unitCount : 0;
        page.mayActivateSlotItemFunc = (_, _, _) => false;
        page.canActivateSlotItemFunc = (_, _, _) => false;
        tabs.AddTab((tabs.count + 1).ToString(), MachineHelper.WrapInIntake(page));
        return page;
    }

    internal static void Refresh(GameItem cell)
    {
        (cell.contentWindow?.childElement as MachineryPanelRow)?.Validate();
        cell.contentWindow?.Validate();
    }

    /// <summary>
    /// Native steps of <see cref="PageSetSwap{T}"/>. A replacement <see cref="MachineryTabs"/> takes the kept page
    /// intakes in order (AddTab reparents their transforms immediately); attaching it through
    /// <c>MachineryPanel.SetContent</c> unlinks and relinks storage in two <c>RelinkStorage</c> calls. Destroying
    /// the original container then only destroys what is still under it: tab captions and the removed empty pages.
    /// </summary>
    private sealed class NativeSwapPort : IPageSetSwapPort<MachineryTabs>
    {
        private readonly GameItem _cell;
        private readonly MachineryPanel _panel;
        private readonly bool _housing;
        private readonly Dictionary<GameGridInventory, GameItem[]> _keptRoots = new();

        internal NativeSwapPort(GameItem cell, bool housing = false)
        {
            _cell = cell;
            _housing = housing;
            _panel = (cell.contentWindow?.childElement as MachineryPanelRow)?.GetPanel(PanelRole)
                ?? throw new InvalidOperationException("The cell pages panel is unavailable.");
        }

        public bool CanOperate => !SaveManager.IsDecoding && CellHealth.Get(_cell).Status != CellHealthStatus.Unavailable &&
            _cell.unitCount == 1 && (!_housing || _cell.identifier == StorageCellDisassemblyRecipe.HousingId &&
                !Client.CellContentWindow.IsMounted(_cell) && Client.CellContentWindow.Component(_cell) == null);

        public MachineryTabs Current => _panel.content as MachineryTabs
            ?? throw new InvalidOperationException("The current pages are unavailable.");

        public LayoutDescriptor? RecordedLayout => CellTags.TryReadLayout(_cell, out var layout) ? layout : null;

        public IReadOnlyList<int> RootCounts() => Pages(Current).Select(page => page.childItems.Count).ToArray();

        public bool CanStart(PageCompaction plan)
        {
            if (!CanOperate || RecordedLayout is not { } layout || !CellPages.IsLinked(_cell, Current, layout)) return false;
            var pages = Pages(Current);
            return plan.Removed.All(index => index < pages.Count && pages[index].childItems.Count == 0);
        }

        public void Prepare(IReadOnlyList<int> keep, Action<MachineryTabs> retain)
        {
            var original = Current;
            var replacement = new MachineryTabs();
            retain(replacement);
            _keptRoots.Clear();
            foreach (var index in keep)
            {
                var element = original.GetPage(index) ?? throw new InvalidOperationException($"Page {index} is missing.");
                if (original.GetTabInventory(index) is GameGridInventory page) _keptRoots[page] = page.childItems.ToArray();
                replacement.AddTab((replacement.count + 1).ToString(), element);
            }
        }

        public void Attach(MachineryTabs pages) => _panel.SetContent(pages);

        public bool IsLinked(MachineryTabs pages, LayoutDescriptor layout)
        {
            if (!CellPages.IsLinked(_cell, pages, layout)) return false;
            // Kept pages must hold exactly the roots they held before the swap.
            foreach (var page in Pages(pages))
            {
                if (_keptRoots.TryGetValue(page, out var roots) &&
                    (page.childItems.Count != roots.Length || roots.Any(root => !page.childItems.Contains(root)))) return false;
            }

            return true;
        }

        public void WriteLayout(LayoutDescriptor layout) => CellTags.WriteLayout(_cell, layout);

        public void SelectFirst(MachineryTabs pages) => pages.Select(0);

        public void ReturnPages(MachineryTabs original)
        {
            for (var index = 0; index < original.count; index++)
            {
                var element = original.GetPage(index);
                if (element == null || element.IsRenderingChildOf(original)) continue;
                element.SetRenderingParent(original);
                element.handler.rectTransform.anchoredPosition = UnityEngine.Vector2.zero;
            }

            original.Validate();
        }

        public void Release(MachineryTabs pages)
        {
            if (pages.handler == null) return;
            if (ReferenceEquals(_panel.content, pages)) throw new InvalidOperationException("Current pages cannot be released.");
            var current = Current;
            for (var index = 0; index < pages.count; index++)
            {
                var element = pages.GetPage(index);
                if (element == null) continue;
                // Pages that moved to the attached container are not part of this tree any more.
                if (!element.IsRenderingChildOf(pages))
                {
                    if (!element.IsRenderingChildOf(current))
                        throw new InvalidOperationException("A kept page is attached nowhere; it would be lost.");
                    continue;
                }

                if (pages.GetTabInventory(index) is not GameGridInventory page || page.childItems.Count != 0 ||
                    page.parents.Count != 0)
                    throw new InvalidOperationException("Only detached empty pages can be released.");
            }

            // A failed native Detach can leave an empty rendering child after clearing panel.content.
            if (((PixelElement)pages).IsRenderingChildOf(_panel)) ((PixelElement)pages).RemoveRenderingParent(_panel);
            if (((PixelElement)pages).IsRenderingChildOf(_panel))
                throw new InvalidOperationException("Attached page rendering cannot be released.");
            UnityEngine.Object.Destroy(pages.handler.gameObject);
        }
    }
}
