using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Composition;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
[NonParallelizable]
internal sealed class WorkbenchUsageTests
{
    private const string Unavailable = "module_refusal_cellunavailable";

    [TestCase(129, 4096L, null)]
    [TestCase(256, 4096L, null)]
    [TestCase(259, 0L, "module_refusal_typesexceedlimit:3")]
    [TestCase(256, 4113L, "module_refusal_bytesexceedcapacity:3")]
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
        Assert.That((bench.LayoutChecks, bench.RootReads), Is.EqualTo((1, 1)));
        Assert.That(fixture.Errors, Is.Empty);
    }

    [Test]
    public void Summary_ShowsTheStorageAndRuleLimitsFromOneBayRead()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(129);
        bench.Refresh.Refresh();
        var row = bench.Cell.contentWindow!.childElement!;
        var before = row.ModulePanelReads;
        var summary = WorkbenchCellSummary.Render(bench.Cell, null, new ModText(), fixture.Usage.Find,
            error => throw new InvalidOperationException("summary failed", error));
        Assert.That(summary, Does.Contain("workbench_info_rules: <color=#ABCDEF>0/142</color>"));
        Assert.That(summary, Does.Contain("workbench_info_types: <color=#ABCDEF>129/352</color>"));
        Assert.That(row.ModulePanelReads - before, Is.EqualTo(1));
        Assert.That(bench.RootReads, Is.EqualTo(1));
    }

    [Test]
    public void ViewOf_ReusesThePageContentsAndReadsTheCurrentModulesOncePerProjection()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(129);
        var row = bench.Cell.contentWindow!.childElement!;
        var before = row.ModulePanelReads;
        var withModule = CellPages.ReadCached(bench.Cell, CellWriteState.Writable);
        Assert.That((withModule.StorageTypeLimit, withModule.PartitionRuleLimit), Is.EqualTo((352, 142)));
        Assert.That(withModule.Upgrades.GetInstalledUpgrades(CellModuleKind.TypeLimit), Is.EqualTo(1));
        Assert.That(row.ModulePanelReads - before, Is.EqualTo(1));

        bench.Bay.childItems.Clear();
        before = row.ModulePanelReads;
        var withoutModule = CellPages.ReadCached(bench.Cell, CellWriteState.Writable);
        Assert.That((withoutModule.StorageTypeLimit, withoutModule.PartitionRuleLimit), Is.EqualTo((256, 128)));
        Assert.That(withoutModule.Upgrades.GetInstalledUpgrades(CellModuleKind.TypeLimit), Is.Zero);
        Assert.That(withoutModule.Usage, Is.SameAs(withModule.Usage));
        Assert.That(withoutModule.Pages[0], Is.SameAs(withModule.Pages[0]));
        Assert.That(row.ModulePanelReads - before, Is.EqualTo(1));
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
        Assert.That(bench.RootReads, Is.EqualTo(1));

        var row = (MachineryPanelRow)bench.Inventory.parent!;
        row.parent = null;
        Assert.That(fixture.Usage.Find(bench.Cell), Is.Null);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(bench.RootReads, Is.EqualTo(1));
    }

    [Test]
    public void PageAndLayoutEvents_InvalidateOnlyTheSeatedCellAndCoalesceUntilRefresh()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(259);
        var other = fixture.Workbench(260);
        bench.Refresh.Refresh();
        other.Refresh.Refresh();
        var page = CellInventoryFixture.Tabs(bench.Cell).GetTabInventory(1)!;
        var root = new GameItem("temporary_root");
        var oldSeatedVersion = CellInventoryCache.Version(bench.Cell);
        var otherVersion = CellInventoryCache.Version(other.Cell);

        page.Place(root);
        page.onSlotAddItemEndFunc?.Invoke(root, page, null);
        var addedVersion = CellInventoryCache.Version(bench.Cell);
        Assert.That(addedVersion, Is.Not.EqualTo(oldSeatedVersion));
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(fixture.Usage.Find(bench.Cell)?.Status, Is.EqualTo(CellModuleUsage.ReadStatus.Pending));
        Assert.That(fixture.Lines(other.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:4" }));
        Assert.That(page.Expel(root), Is.True);
        page.onSlotRemoveItemEndFunc?.Invoke(root, page, null);
        var removedVersion = CellInventoryCache.Version(bench.Cell);
        Assert.That(removedVersion, Is.Not.EqualTo(addedVersion));
        bench.SetContents(268);
        CellPages.NotifyLayoutWritten(bench.Cell);
        Assert.That(CellInventoryCache.Version(bench.Cell), Is.Not.EqualTo(removedVersion));
        var unseated = new GameItem(StorageCellTier.All[0].CellId) { parentInventory = bench.Inventory };
        var unseatedVersion = CellInventoryCache.Version(unseated);
        CellPages.NotifyLayoutWritten(unseated);

        Assert.That((bench.LayoutChecks, bench.RootReads), Is.EqualTo((1, 1)));
        Assert.That(CellInventoryCache.Version(unseated), Is.EqualTo(unseatedVersion));
        Assert.That(CellInventoryCache.Version(other.Cell), Is.EqualTo(otherVersion));
        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(other.Refresh.Refresh(), Is.False);
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:12" }));
        Assert.That((bench.RootReads, other.RootReads), Is.EqualTo((2, 1)));
    }

    [Test]
    public void ModuleAndSummaryChanges_UseCurrentBayAndReuseUsage()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(261);
        bench.Refresh.Refresh();
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:5" }));

        var added = TypeModule();
        bench.Bay.Place(added);
        CellUpgradeInventory.RaiseModulesChanged(bench.Cell);
        fixture.Usage.RefreshSummary(bench.Cell); // Same summary handler used for partition commits.
        Assert.That(bench.SummaryCounts, Is.EqualTo(new[] { 261, 261 }));
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.Refresh.Refresh(), Is.False);

        bench.Bay.childItems.Remove(added);
        CellUpgradeInventory.RaiseModulesChanged(bench.Cell);
        Assert.That(fixture.Lines(added), Is.Empty, "A stale parent alone is insufficient for membership.");
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:5" }));
        Assert.That((bench.LayoutChecks, bench.RootReads), Is.EqualTo((1, 1)));
    }

    [Test]
    public void VisibilityAndReplacement_InvalidateOldUsageAcrossCloseReopenAndDetach()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(259);
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
        bench.SeatNewCell(263);
        Assert.That(fixture.Usage.Find(oldCell), Is.Null);
        Assert.That(fixture.Lines(oldModule), Is.Empty);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        bench.Refresh.Refresh();
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { "module_refusal_typesexceedlimit:7" }));

        bench.Refresh.Detach(); // Window destruction.
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        var other = fixture.Workbench(258);
        Assert.That(other.Refresh.Refresh(), Is.True);
        fixture.Usage.RefreshSummary(other.Cell);
        fixture.Usage.Detach();
        Assert.That(other.SummaryCounts, Is.EqualTo(new int?[] { 258, null }));
        Assert.That(fixture.Lines(other.Module), Is.Empty);
        Assert.That(other.Refresh.Refresh(), Is.False);
        Assert.That((other.LayoutChecks, other.RootReads), Is.EqualTo((1, 1)));
    }

    [Test]
    public void Decode_SuppressesPreviewAndDefersNewSeatRead()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(259);
        bench.Refresh.Refresh();
        SaveManager.IsDecoding = true;
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        bench.SeatNewCell(265);
        CellPages.NotifyLayoutWritten(bench.Cell);
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.RootReads, Is.Zero);

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
        var bench = fixture.Workbench(259);
        var equal = new GameItem(CellUpgradeInventory.EqualDistributionId);
        bench.Bay.Place(equal);
        bench.Refresh.Refresh();
        CellPages.NotifyLayoutWritten(bench.Cell);
        Assert.That(fixture.Lines(equal), Is.Empty);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        bench.FailRead(failure);

        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(fixture.Usage.Find(bench.Cell)?.Status, Is.EqualTo(CellModuleUsage.ReadStatus.Unavailable));
        Assert.That(fixture.Usage.Find(bench.Cell)?.StoredTypes, Is.Zero);
        Assert.That(fixture.Lines(bench.Module), Is.EqualTo(new[] { Unavailable }));
        Assert.That(fixture.Lines(equal), Is.EqualTo(blockEqual ? new[] { Unavailable } : Array.Empty<string>()));
        Assert.That(bench.Refresh.Refresh(), Is.False);
        Assert.That(bench.LayoutChecks, Is.EqualTo(2));
        Assert.That(fixture.Errors, Has.Count.EqualTo(failure == "unreadable" ? 1 : 0));

        bench.RestoreLayout();
        bench.SetContents(128);
        CellPages.NotifyLayoutWritten(bench.Cell);
        Assert.That(bench.Refresh.Refresh(), Is.True);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
    }

    [Test]
    public void PassiveHighlight_SkipsLiveReadOnlyForInstalledWorkbenchCapacityModules()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(259);
        var module = bench.Module;
        module.RemovalProbe = () => CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, module);

        Assert.That(WorkbenchModuleHighlight.MayRemove(module, null), Is.False);
        Assert.That((bench.LayoutChecks, bench.RootReads), Is.EqualTo((0, 0)));
        Assert.That(WorkbenchModuleHighlight.MayRemove(module, module), Is.False);
        Assert.That((bench.LayoutChecks, bench.RootReads), Is.EqualTo((1, 1)));

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
        var bench = fixture.Workbench(256, 4096);
        bench.Refresh.Refresh();
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, bench.Module), Is.True);
        bench.SetContents(256, 4097);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, bench.Module), Is.False);
        bench.SetContents(257);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, bench.Module), Is.False);
        Assert.That(fixture.Lines(bench.Module), Is.Empty);
        Assert.That(bench.RootReads, Is.EqualTo(4));
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
        bench.FailRead(failure);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, bench.Module), Is.False);
        Assert.That(bench.RootReads, Is.EqualTo(1 + expectedReads));
    }

    [Test]
    public void InstallationAndEqualDistributionRemoval_DoNotReadUsage()
    {
        using var fixture = new Fixture();
        var bench = fixture.Workbench(259);
        var equal = new GameItem(CellUpgradeInventory.EqualDistributionId);
        bench.Bay.Place(equal);
        Assert.That(CellUpgradeCallbacks.CanInstall(CellUpgradeInventory.InstallationState(bench.Cell), CellModuleKind.TypeLimit).IsAllowed, Is.True);
        Assert.That(CellUpgradeInventory.CanRemove(bench.Cell, bench.Bay, equal), Is.True);
        Assert.That(bench.RootReads, Is.Zero);
    }

    private static GameItem TypeModule()
    {
        var item = new GameItem(CellUpgradeInventory.TypeLimitId);
        ModuleTags.Write(item, new TypeLimitRoll(ModuleQuality.Common, 8, 5));
        return item;
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
        internal int RootReads => CellInventoryFixture.Tabs(Cell).GetTabInventory(0)!.childItems.SnapshotReads;
        internal int LayoutChecks => Cell.contentWindow!.childElement!.StorageReads;

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
            if (Inventory.childItem is { } previous) Inventory.Expel(previous);
            var cell = CellInventoryFixture.Create();
            var tabs = CellInventoryFixture.Tabs(cell);
            var layout = LayoutDescriptor.AnchorOnly();
            var strip = PageGeometry.Strip(new BoundingSize(1, 1), 16);
            // Leave one spare strip so live inventory changes do not require a layout event.
            var pageCount = (types + strip.SlotCount - 1) / strip.SlotCount + 1;
            for (var index = 0; index < pageCount; index++)
            {
                CellPages.AddPage(tabs, strip);
                layout = layout.WithAppended(strip);
            }
            CellTags.WriteLayout(cell, layout);
            Inventory.Place(cell);
            Bay.Place(TypeModule());
            SetContents(types, area);
        }

        internal void SetContents(int types, long area = 0)
        {
            var tabs = CellInventoryFixture.Tabs(Cell);
            var nextType = 0;
            for (var index = 1; index < tabs.count; index++)
            {
                var page = tabs.GetTabInventory(index)!;
                while (page.childItems.Count > 0) page.Expel(page.childItems[^1]);
                for (var x = 0; x < page.inventoryShape.width && nextType < types; x++, nextType++)
                {
                    var units = nextType == 0 ? checked((int)Math.Max(1, area - types + 1)) : 1;
                    page.Place(new GameItem($"fixture_{nextType}")
                    {
                        modifiedShape = new GridShape(x, 0, 1, 1),
                        unitCount = units
                    });
                }
            }
            if (nextType != types) throw new InvalidOperationException("Not enough fixture slots.");
        }

        internal void FailRead(string failure)
        {
            var page = CellInventoryFixture.Tabs(Cell).GetTabInventory(1)!;
            switch (failure)
            {
                case "incomplete":
                    page.inventoryShape.width--;
                    break;
                case "unavailable":
                    CellHealth.Set(Cell, new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.InvalidLayout));
                    break;
                case "unreadable":
                    page.childItems.Add(new GameItem("unowned_root"));
                    break;
                case "protected":
                    page.Place(new GameItem("off_grid_root") { modifiedShape = new GridShape(0, 1, 1, 1) });
                    break;
                default:
                    throw new ArgumentException("Unknown fixture failure.", nameof(failure));
            }
        }

        internal void RestoreLayout()
        {
            var page = CellInventoryFixture.Tabs(Cell).GetTabInventory(1)!;
            page.inventoryShape.width = 16;
            page.childItems.RemoveAll(root => root.parentInventory != page);
            CellHealth.Set(Cell, CellHealthState.Available);
        }
    }

    private sealed class Settings : IGridPowerSettingsPort
    {
        public int GetPowerUsage(GridNodeKind kind) => throw new InvalidOperationException("unexpected network scan");
    }
}
