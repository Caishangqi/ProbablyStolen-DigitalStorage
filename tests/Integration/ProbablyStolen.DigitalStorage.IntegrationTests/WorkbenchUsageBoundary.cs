using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

internal sealed class ItemMouseHandler
{
    internal readonly GameItem? currentItem = null;
}

internal sealed class PixelWindow : GraphNodeStorage
{
    internal readonly List<GameItem> parentItems = new();
    internal MachineryPanelRow? childElement;
}

internal sealed class MachineryPanelRow : GraphNodeStorage
{
    internal GraphNodeStorage? parent;
    internal MachineryPanel? Modules;
    internal int ModulePanelReads { get; private set; }
    internal MachineryPanel? GetPanel(string role)
    {
        if (role != "cell_modules") return null;
        ModulePanelReads++;
        return Modules;
    }
}

internal sealed class MachineryPanel
{
    internal MachineryIntake? content;
}

internal sealed class MachineryIntake
{
    internal GameGridInventory? content;
}

namespace ProbablyStolen.DigitalStorage.Platform.Storage
{
    internal static partial class CellPages
    {
        private static readonly ConditionalWeakTable<GameItem, PageReadSpy> Spies = new();

        internal static PageReadSpy Spy(GameItem cell) => Spies.GetValue(cell, _ => new PageReadSpy());

        internal static CellView ProjectContents(GameItem cell, CellView contents) =>
            ViewOf(cell, contents.Pages, contents.Usage, CellWriteState.Writable);

        internal static IReadOnlyList<GameGridInventory> Pages(GameItem cell) => cell.StoragePages;

        internal static bool IsComplete(GameItem cell)
        {
            var spy = Spy(cell);
            spy.LayoutChecks++;
            return spy.Complete;
        }

        internal static CellView Read(GameItem cell, Func<GameItem, ItemKey?> identify, CellWriteState writeState)
        {
            var spy = Spy(cell);
            spy.LiveReads++;
            return spy.ReadLive();
        }

        internal static CellView ReadCached(GameItem cell, CellWriteState writeState)
        {
            var spy = Spy(cell);
            spy.CachedReads++;
            return spy.CachedView ??= spy.ReadCached();
        }
    }

    internal sealed class PageReadSpy
    {
        internal bool Complete = true;
        internal bool Unavailable;
        internal int LayoutChecks;
        internal int CachedReads;
        internal int LiveReads;
        internal int CacheDrops;
        internal CellView? CachedView;
        internal Func<CellView> ReadCached = () => throw new InvalidOperationException("unexpected cached read");
        internal Func<CellView> ReadLive = () => throw new InvalidOperationException("unexpected live read");
    }

    internal static class CellInventoryCache
    {
        internal static void Forget(GameItem cell)
        {
            var spy = CellPages.Spy(cell);
            spy.CacheDrops++;
            spy.CachedView = null;
        }
    }

    internal static class CellHealth
    {
        internal static bool IsUnavailable(GameItem cell) => CellPages.Spy(cell).Unavailable;
        internal static CellHealthState Get(GameItem cell) => CellHealthState.Available;
        internal static void Set(GameItem cell, CellHealthState state) =>
            throw new InvalidOperationException("unexpected health write");
    }

    internal static class DrivePriorityTag
    {
        internal static int ReadForCell(GameItem cell) => DrivePriority.Default;
    }

    internal sealed class CellPartitionTags
    {
        internal CellPartitionTags(GameItem cell) { }
        internal CellPartitionTags Capture() => this;
        internal (PartitionConfig Config, bool Valid) Interpret() => (PartitionConfig.Empty, true);
        internal static CellPartition Read(GameItem cell) => CellPartition.Unfiltered;
    }

    internal static class ModuleBayExpansionHooks
    {
        internal static void Initialize(GameItem cell) => throw new InvalidOperationException("unexpected bay expansion");
    }

    internal static class StorageService
    {
        internal static ItemKey? Identify(GameItem item) => throw new InvalidOperationException("unexpected native identity read");
    }
}
