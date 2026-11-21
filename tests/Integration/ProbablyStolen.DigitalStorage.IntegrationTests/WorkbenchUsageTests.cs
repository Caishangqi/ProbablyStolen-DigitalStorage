using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Composition;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
[NonParallelizable]
internal sealed class WorkbenchUsageTests
{
    private static readonly StorageCellTier Tier = StorageCellTier.All[0];
    private const string Unavailable = "module_refusal_cellunavailable";

    [TestCase(128, 4096L, null)]
    [TestCase(131, 0L, "module_refusal_typesexceedlimit:3")]
    [TestCase(128, 4113L, "module_refusal_bytesexceedcapacity:3")]
    public void Tooltip_UsesReadyUsageWithoutProbingPages(int types, long area, string? refusal)
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(types, area);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        for (var frame = 0; frame < 100; frame++)
        {
            Assert.That(fixture.Lines(bench.Module), Is.EqualTo(refusal == null ? Array.Empty<string>() : new[] { refusal }));
            Assert.That(bench.Refresh.Refresh(), Is.False);
        }
        Assert.That((bench.Spy.LayoutChecks, bench.Spy.CachedReads, bench.Spy.LiveReads), Is.EqualTo((1, 1, 0)));
        Assert.That(fixture.Errors, Is.Empty);
    }

    [Test]
    public void Refresh_UsesNativePanelRowToDisplayStoredTypesAndRejectsDetachedRows()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(124);
        fixture.Usage.RefreshSummary(bench.Cell);
        Assert.That(bench.SummaryCounts, Is.EqualTo(new int?[] { null }));

        Assert.That(bench.Refresh.Refresh(), Is.True);
        fixture.Usage.RefreshSummary(bench.Cell);
        Assert.That(bench.SummaryCounts, Is.EqualTo(new int?[] { null, 124 }));
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(bench.Spy.CachedReads, Is.EqualTo(1));

        var row = (MachineryPanelRow)bench.Inventory.parent!;
        row.parent = null;
        Assert.That(fixture.Usage.Find(bench.Cell), Is.Null);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(bench.Spy.CachedReads, Is.EqualTo(1));
    }

    [Test]
    public void PageAndLayoutEvents_InvalidateOnlyTheSeatedCellAndCoalesceUntilRefresh()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(131);
        var other = fixture.Workbench(132);
        bench.Refresh.Refresh();
        other.Refresh.Refresh();
        var page = new GameGridInventory { parent = bench.Cell.contentWindow!.childElement };
        var root = new GameItem("root");
        CellPages.ObserveRootChanges(page);
        bench.Spy.ReadCached = () => Contents(140);

        page.Place(root);
        page.onSlotAddItemEndFunc?.Invoke(root, page, null);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(fixture.Usage.Find(bench.Cell)?.Status, Is.EqualTo(CellModuleUsage.ReadStatus.Pending));
        Assert.That(fixture.Lines(other.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:4" }));
        page.childItems.Remove(root);
        root.parentInventory = null;
        page.onSlotRemoveItemEndFunc?.Invoke(root, page, null);
        CellPages.NotifyLayoutWritten(bench.Cell);
        var unseated = new GameItem(Tier.CellId) { parentInventory = bench.Inventory };
        CellPages.NotifyLayoutWritten(unseated);

        Assert.That((bench.Spy.LayoutChecks, bench.Spy.CachedReads, bench.Spy.CacheDrops), Is.EqualTo((1, 1, 3)));
        Assert.That(CellPages.Spy(unseated).CacheDrops, Is.Zero);
        Assert.That(other.Spy.CacheDrops, Is.Zero);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(other.Refresh.Refresh(), Is.False);
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:12" }));
        Assert.That((bench.Spy.CachedReads, other.Spy.CachedReads), Is.EqualTo((2, 1)));
    }

    [Test]
    public void ModuleAndSummaryChanges_UseCurrentBayAndReuseUsage()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(133);
        bench.Refresh.Refresh();
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:5" }));

        var added = new GameItem(CellUpgradeInventory.TypeLimitId);
        bench.Bay.Place(added);
        CellUpgradeInventory.RaiseModulesChanged(bench.Cell);
        fixture.Usage.RefreshSummary(bench.Cell); // Same summary handler used for partition commits.
        Assert.That(bench.SummaryCounts, Is.EqualTo(new[] { 133, 133 }));
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.Refresh.Refresh(), Is.False);

        bench.Bay.childItems.Remove(added);
        CellUpgradeInventory.RaiseModulesChanged(bench.Cell);
        Assert.That(fixture.Lines(added), Is.Empty, "A stale parent alone is insufficient for membership.");
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:5" }));
        Assert.That((bench.Spy.LayoutChecks, bench.Spy.CachedReads, bench.Spy.LiveReads), Is.EqualTo((1, 1, 0)));
    }

    [Test]
    public void VisibilityAndReplacement_InvalidateOldUsageAcrossCloseReopenAndDetach()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(131);
        bench.Refresh.Refresh();
        bench.Visible = false;
        bench.Refresh.Invalidate(); // WorkbenchUsageRefresh.OnDisable, including PixelWindow.Hide.
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        bench.Visible = true;
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:3" }));

        var oldCell = bench.Cell;
        var oldModule = bench.Module;
        bench.SeatNewCell(135);
        Assert.That(fixture.Usage.Find(oldCell), Is.Null);
        Assert.That(fixture.Lines(oldModule), Is.Empty);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        bench.Refresh.Refresh();
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:7" }));

        bench.Refresh.Detach(); // Window destruction.
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        var other = fixture.Workbench(130);
        Assert.That(other.Refresh.Refresh(), Is.True);
        fixture.Usage.RefreshSummary(other.Cell);
        fixture.Usage.Detach();
        Assert.That(other.SummaryCounts, Is.EqualTo(new int?[] { 130, null }));
        Assert.That(fixture.Lines(other.Module), Is.Empty);
        Assert.That(other.Refresh.Refresh(), Is.False);
        Assert.That((other.Spy.LayoutChecks, other.Spy.CachedReads, other.Spy.LiveReads), Is.EqualTo((1, 1, 0)));
    }

    [Test]
    public void Decode_SuppressesPreviewAndDefersNewSeatRead()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(131);
        bench.Refresh.Refresh();
        SaveManager.IsDecoding = true;
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        bench.SeatNewCell(137);
        CellPages.NotifyLayoutWritten(bench.Cell);
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.Spy.CachedReads, Is.Zero);

        SaveManager.IsDecoding = false;
        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:9" }));
    }

    [TestCase("incomplete", true)]
    [TestCase("unreadable", false)]
    [TestCase("protected", false)]
    public void FailedRefresh_HidesCountsAndOnlyBlocksEqualDistributionForUnavailableLayout(string failure, bool blockEqual)
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(131);
        var equal = new GameItem(CellUpgradeInventory.EqualDistributionId);
        bench.Bay.Place(equal);
        bench.Refresh.Refresh();
        CellPages.NotifyLayoutWritten(bench.Cell);
        Assert.That(fixture.Lines(equal), Is.Empty);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        FailRead(bench.Spy, failure);

        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(fixture.Usage.Find(bench.Cell)?.Status, Is.EqualTo(CellModuleUsage.ReadStatus.Unavailable));
        Assert.That(fixture.Usage.Find(bench.Cell)?.StoredTypes, Is.Zero);
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { Unavailable }));
        Assert.That(fixture.Lines(equal), Is.EqualTo(blockEqual ? new[] { Unavailable } : Array.Empty<string>()));
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(bench.Spy.LayoutChecks, Is.EqualTo(2));
        Assert.That(bench.Spy.LiveReads, Is.Zero);
        Assert.That(fixture.Errors, Has.Count.EqualTo(failure == "unreadable" ? 1 : 0));

        bench.Spy.Complete = true;
        bench.Spy.ReadCached = () => Contents(128);
        CellPages.NotifyLayoutWritten(bench.Cell);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
    }

    [Test]
    public void PassiveHighlight_SkipsLiveReadOnlyForInstalledWorkbenchCapacityModules()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(131);
        var module = bench.Module;
        module.RemovalProbe = () => CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, module);

        Assert.That(WorkbenchModuleHighlight.MayRemove(module, null), Is.False);
        Assert.That((bench.Spy.LayoutChecks, bench.Spy.LiveReads), Is.EqualTo((0, 0)));
        Assert.That(WorkbenchModuleHighlight.MayRemove(module, module), Is.False);
        Assert.That((bench.Spy.LayoutChecks, bench.Spy.LiveReads), Is.EqualTo((1, 1)));

        var equal = new GameItem(CellUpgradeInventory.EqualDistributionId) { RemovalProbe = () => true };
        bench.Bay.Place(equal);
        Assert.That(WorkbenchModuleHighlight.MayRemove(equal, null), Is.True);
        module.RemovalProbe = () => true;
        bench.Bay.childItems.Remove(module);
        Assert.That(WorkbenchModuleHighlight.MayRemove(module, null), Is.True);
        bench.Bay.Place(module);
        bench.Inventory.identifier = "not_a_workbench";
        Assert.That(WorkbenchModuleHighlight.MayRemove(module, null), Is.True);
    }

    [Test]
    public void Removal_UsesLiveUsageIndependentlyOfAnAllowedPreview()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(128, 4096);
        bench.Refresh.Refresh();
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, bench.Module), Is.True);
        bench.Spy.ReadLive = () => Contents(128, 4097);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, bench.Module), Is.False);
        bench.Spy.ReadLive = () => Contents(129);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, bench.Module), Is.False);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That((bench.Spy.CachedReads, bench.Spy.LiveReads), Is.EqualTo((1, 3)));
    }

    [TestCase("incomplete", 0)]
    [TestCase("unavailable", 0)]
    [TestCase("unreadable", 1)]
    [TestCase("protected", 1)]
    public void Removal_ValidReductionFailsClosedWhenLiveUsageIsUnknown(string failure, int expectedReads)
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(0);
        bench.Refresh.Refresh();
        FailRead(bench.Spy, failure);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, bench.Module), Is.False);
        Assert.That((bench.Spy.CachedReads, bench.Spy.LiveReads), Is.EqualTo((1, expectedReads)));
    }

    [Test]
    public void InstallationAndEqualDistributionRemoval_DoNotReadUsage()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(131);
        var equal = new GameItem(CellUpgradeInventory.EqualDistributionId);
        bench.Bay.Place(equal);
        Assert.That(CellUpgradeCallbacks.CanInstall(CellUpgradeInventory.InstallationState(bench.Cell), CellModuleKind.TypeLimit).IsAllowed, Is.True);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, equal), Is.True);
        Assert.That((bench.Spy.CachedReads, bench.Spy.LiveReads), Is.EqualTo((0, 0)));
    }

    private static void FailRead(PageReadSpy spy, string failure)
    {
        if (failure == "incomplete") spy.Complete = false;
        else if (failure == "unavailable") spy.Unavailable = true;
        else
        {
            spy.ReadCached = spy.ReadLive = failure == "protected"
                ? () => Contents(0, protectedPage: true)
                : () => throw new InvalidOperationException("injected usage read failure");
        }
    }

    private static CellView Contents(int types, long area = 0, bool protectedPage = false)
    {
        var usage = new CellUsage();
        for (var index = 0; index < types; index++)
            usage.Add(new ItemKey($"fixture_{index}"), 1, index == 0 ? Math.Max(1, area - types + 1) : 1);
        var pages = new List<PageView> { PageView.Unslotted(PageGeometry.Anchor, 0) };
        if (protectedPage) pages.Add(PageView.Protected(PageGeometry.Strip(new BoundingSize(1, 1), 16), 1));
        return new CellView(CellRules.For(Tier), Tier.GetTotalTypes(), pages, usage, CellPartition.Unfiltered);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly List<string> Errors = new();
        internal readonly WorkbenchUsage Usage;
        private readonly TooltipComposition _composition;

        internal Fixture()
        {
            var text = new ModText();
            Usage = new WorkbenchUsage(error => Errors.Add(error.Message));
            CellPages.UsageChanged += Usage.InvalidateContents;
            CellUpgradeInventory.ModulesChanged += Usage.RefreshSummary;
            _composition = new TooltipComposition(new NativeNetworkScanner(new Settings()), new GridNetworkPowerCoordinator(),
                text, new BasicCellHandler(text), Usage.Find, Errors.Add);
            _composition.Enable();
        }

        internal Bench Workbench(int types, long area = 0) => new(Usage, types, area);

        internal List<string> Lines(GameItem item)
        {
            var builder = new RichTextBuilder();
            ModHook.RaiseTooltip(builder, item);
            return builder.Lines;
        }

        public void Dispose()
        {
            _composition.Dispose();
            CellPages.UsageChanged -= Usage.InvalidateContents;
            CellUpgradeInventory.ModulesChanged -= Usage.RefreshSummary;
            Usage.Detach();
            SaveManager.IsDecoding = false;
        }
    }

    private sealed class Bench
    {
        internal readonly GameSlotInventory Inventory = new() { identifier = CellUpgradeInventory.WorkbenchSlotId };
        internal readonly WorkbenchUsage.Slot Refresh;
        internal readonly List<int?> SummaryCounts = new();
        internal bool Visible = true;
        internal GameItem Cell => Inventory.childItem!;
        internal GameGridInventory Bay => CellUpgradeInventory.Bay(Cell)!;
        internal GameItem Module => Bay.childItems[0];
        internal PageReadSpy Spy => CellPages.Spy(Cell);

        internal Bench(WorkbenchUsage usage, int types, long area)
        {
            SeatNewCell(types, area);
            var window = new PixelWindow();
            var workbench = new GameItem("digital_storage.cell_workbench") { contentWindow = window };
            window.parentItems.Add(workbench);
            window.childElement = new MachineryPanelRow { parent = window };
            Inventory.parent = window.childElement;
            Refresh = usage.Register(Inventory, workbench, window, () => Visible,
                () => SummaryCounts.Add(usage.Find(Cell) is { Status: CellModuleUsage.ReadStatus.Ready } snapshot
                    ? snapshot.StoredTypes : null));
        }

        internal void SeatNewCell(int types, long area = 0)
        {
            if (Inventory.childItem is { } previous) previous.parentInventory = null;
            var cell = new GameItem(Tier.CellId) { parentInventory = Inventory, contentWindow = new PixelWindow() };
            Inventory.childItem = cell;
            cell.contentWindow.parentItems.Add(cell);
            var bay = new GameGridInventory { identifier = CellUpgradeInventory.BayId };
            var row = new MachineryPanelRow
            {
                parent = cell.contentWindow,
                Modules = new MachineryPanel { content = new MachineryIntake { content = bay } }
            };
            cell.contentWindow.childElement = row;
            bay.parent = row;
            bay.Place(new GameItem(CellUpgradeInventory.TypeLimitId));
            Spy.ReadCached = Spy.ReadLive = () => Contents(types, area);
        }
    }

    private sealed class Settings : IGridPowerSettingsPort
    {
        public int GetPowerUsage(GridNodeKind kind) => throw new InvalidOperationException("unexpected network scan");
    }
}
