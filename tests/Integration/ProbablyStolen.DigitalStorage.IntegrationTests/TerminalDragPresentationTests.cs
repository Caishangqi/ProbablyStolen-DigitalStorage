using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class TerminalDragPresentationTests
{
    [Test]
    public void MergedPage_UsesWholeGroupForSearchAndHalfRequest()
    {
        var flow = new Flow();
        var half = Container("bottle", 4, 5, "half");
        var full = Container("bottle", 9, 4, "full");
        var empty = Container("bottle", 0, 2, "empty");
        flow.Load(half, full, empty);
        flow.Menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        flow.Refresh();
        flow.Screen.SetSearchText("#half");
        var page = flow.Page();
        var group = page.Slots[0].Group!;
        var amount = new DragAmountModel();
        Assert.That(group.Total, Is.EqualTo(11));
        Assert.That(amount.Begin(group.Total, allPieces: true), Is.True);
        Assert.That(amount.Halve(group.Total), Is.EqualTo(6));

        var batch = flow.Menu.CreateDragBatch(page, page, new[] { group.Id }, flow.Pipeline.Revision, amount.RequestedPieces);

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.Steps.Select(step => (step.Key, step.Pieces)), Is.EqualTo(new[] { (half.Key, 5L), (full.Key, 1L) }));
    }

    [Test]
    public void MergedAndSingleGroups_UseFinalPageOrderAndFullTotals()
    {
        var flow = new Flow();
        var first = Container("fillet", 1, 30, "one");
        var second = Container("fillet", 2, 59, "two");
        var apple = new TerminalEntry(new ItemKey("apple"), 3, "Apple", "sprite");
        flow.Load(first, second, apple);
        var page = flow.Page();
        var groups = page.Slots.Where(slot => slot.Group != null).Select(slot => slot.Group!).ToArray();
        var mixed = groups.Single(group => group.IsMixed);
        Assert.That(mixed.Total, Is.EqualTo(89));
        var amount = new DragAmountModel();
        amount.Begin(mixed.Total, allPieces: true);
        Assert.That(amount.Halve(mixed.Total), Is.EqualTo(45));

        var batch = flow.Menu.CreateDragBatch(page, page, groups.Reverse().Select(group => group.Id), flow.Pipeline.Revision);

        Assert.That(batch!.SelectedGroupIds, Is.EqualTo(groups.Select(group => group.Id)));
        Assert.That(batch.RequestedPieces, Is.EqualTo(92));
        Assert.That(batch.Steps.Where(step => step.GroupId == mixed.Id).Select(step => step.Key),
            Is.EqualTo(mixed.Members.Select(member => member.Key)));
    }

    [Test]
    public void FilteredOutGroupAndPausedZeroPlaceholderCannotBeSelected()
    {
        var flow = new Flow(columns: 1);
        var apple = new TerminalEntry(new ItemKey("apple"), 1, "Apple", "sprite");
        var banana = new TerminalEntry(new ItemKey("banana"), 1, "Banana", "sprite");
        flow.Load(apple, banana);
        var firstPage = flow.Page();
        var offScreenId = flow.Menu.GetEntries()[1].Id;
        Assert.That(firstPage.Slots.Select(slot => slot.Group?.Id), Does.Not.Contain(offScreenId));
        Assert.That(flow.Menu.CreateDragBatch(firstPage, firstPage, new[] { offScreenId }, flow.Pipeline.Revision),
            Is.Not.Null, "A group below the shown rows is still part of the filtered list.");
        flow.Screen.SetSearchText("apple");
        var filtered = flow.Page();
        Assert.That(flow.Menu.CreateDragBatch(filtered, filtered, new[] { offScreenId }, flow.Pipeline.Revision), Is.Null);
        flow.Screen.SetSearchText(string.Empty);

        flow.Menu.SetPaused(true);
        flow.Menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[] { banana }));
        flow.Screen.UpdateEntriesInOrder(flow.Menu.GetEntries());
        var placeholder = flow.Page();
        Assert.That(placeholder.Slots[0].Group!.Total, Is.Zero);
        Assert.That(flow.Menu.CreateDragBatch(placeholder, placeholder, new[] { placeholder.Slots[0].Group!.Id },
            flow.Pipeline.Revision), Is.Null);
    }

    [Test]
    public void ModuleOrRulesChange_InvalidatesOldPageBeforeRegroupAndUsesIdentityAfterward()
    {
        var flow = new Flow();
        flow.Load(Container("fillet", 1, 30, "one"), Container("fillet", 2, 59, "two"));
        var shown = flow.Page();
        var selected = shown.Slots[0].Group!.Id;
        flow.Pipeline.Invalidate();
        Assert.That(flow.Menu.CreateDragBatch(shown, shown, new[] { selected }, flow.Pipeline.Revision), Is.Null);

        flow.Installed = false;
        flow.Refresh();
        var exact = flow.Page();
        Assert.That(exact.Slots.Count(slot => slot.Group != null), Is.EqualTo(2));
        Assert.That(flow.Menu.CreateDragBatch(exact, exact, new[] { exact.Slots[0].Group!.Id }, flow.Pipeline.Revision), Is.Not.Null);

        flow.Installed = true;
        flow.Data.Value = StackingConfigCodec.Encode(StackingConfigCodec.Decode(flow.Data.Value).Config!.WithAllEnabled(false));
        flow.Pipeline.Invalidate();
        flow.Refresh();
        Assert.That(flow.Page().Slots.Count(slot => slot.Group != null), Is.EqualTo(2));
    }

    [Test]
    public void InvalidProjector_FallsBackToExactGroupsAndStaleOrderIsRejected()
    {
        var flow = new Flow();
        flow.Load(Container("bottle", 4, 5, "half"), Container("bottle", 9, 4, "full"));
        flow.Menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        flow.Refresh();
        var shown = flow.Page();
        var group = shown.Slots[0].Group!;
        flow.Menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Ascending);
        Assert.That(flow.Menu.CreateDragBatch(shown, shown, new[] { group.Id }, flow.Pipeline.Revision), Is.Null);

        var invalidPipeline = new StoragePresentationPipeline(new[] { new InvalidProjector() }, Array.Empty<ISlotDecorator>(),
            Array.Empty<IEntryTooltipContributor>(), Array.Empty<TooltipPanelDefinition>(), _ => { });
        var context = flow.Context();
        flow.Menu.SetProjection(entries => invalidPipeline.Project(entries, context, EntryAttributes.Empty));
        flow.Screen.UpdateEntriesInOrder(flow.Menu.GetEntries());
        var fallback = flow.Screen.GetPage(invalidPipeline.Revision);
        Assert.That(fallback.Slots.Count(slot => slot.Group != null), Is.EqualTo(2));
        Assert.That(flow.Menu.CreateDragBatch(fallback, fallback, new[] { fallback.Slots[0].Group!.Id }, invalidPipeline.Revision), Is.Not.Null);
    }

    private static TerminalEntry Container(string id, int count, long pieces, string tag)
    {
        const string nested = "44534944000000010a010100000001720000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001010000000102000000000000000000000001000000010000000000000000000100000000010b00000001000000000000000000000001000000010000000000000000000100000000000000000000000001000000010000000000000000000100010100000001630000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000100000000000000000000000301030000000004000000000000000000000000000000000000000000000000000000000000010000000166010000000000000000";
        var child = (0x63 + count).ToString("x2");
        var bytes = Convert.FromHexString(nested.Replace("0000000163", "00000001" + child, StringComparison.Ordinal));
        var key = ItemKey.FromCanonical("r", bytes);
        return new TerminalEntry(key, pieces, id, "sprite", document: new SearchDocument(id, id, new[] { tag }));
    }

    private sealed class Flow
    {
        internal readonly StorageMenu Menu = new();
        internal readonly StorageScreen Screen;
        internal readonly MemoryData Data = new();
        internal readonly StoragePresentationPipeline Pipeline;
        internal bool Installed = true;

        internal Flow(int columns = 4)
        {
            Screen = new StorageScreen(columns, 1);
            Data.Value = StackingConfigCodec.Encode(new StackingConfig(new[]
            {
                new StackingItemRule("r", true, new[]
                {
                    new StackingRule(StackingFieldCatalog.ContainerContent, StackingMode.Ignore),
                    new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Ignore)
                })
            }));
            Pipeline = new StoragePresentationPipeline(new[] { new StackingProjector(_ => { }, () => false) },
                Array.Empty<ISlotDecorator>(), Array.Empty<IEntryTooltipContributor>(), Array.Empty<TooltipPanelDefinition>(), _ => { });
        }

        internal TerminalContext Context() => new(Installed ? new[] { StackingModule.ModuleId } : Array.Empty<string>(),
            false, TerminalRegistry.StorageTerminalName, new NoCommands(), new object(), Data);
        internal void Load(params TerminalEntry[] entries)
        {
            Menu.BroadcastChanges(new TerminalInventorySnapshot(true, entries));
            Refresh();
        }
        internal void Refresh()
        {
            var context = Context();
            Menu.SetProjection(entries => Pipeline.Project(entries, context, EntryAttributes.Empty));
            Screen.UpdateEntriesInOrder(Menu.GetEntries());
        }
        internal StorageScreenPage Page() => Screen.GetPage(Pipeline.Revision);
    }

    private sealed class MemoryData : ITerminalFeatureData
    {
        internal string? Value;
        public string? Read(string key) => Value;
        public bool TryWrite(string key, string value) { Value = value; return true; }
    }

    private sealed class NoCommands : ITerminalCommands
    {
        public void PushScreen(ITerminalScreen screen) { }
        public void PopScreen() { }
        public void PopToView() { }
        public void ClearSelection() { }
        public void ShowMessage(string textKey) { }
        public void StoreState() { }
        public void Switch(string terminalName) { }
        public void NotifySettingChanged(string settingName) { }
    }

    private sealed class InvalidProjector : IStorageEntryProjector
    {
        public string Id => "invalid";
        public IReadOnlyCollection<string> RequiredAttributes => Array.Empty<string>();
        public bool IsActive(TerminalContext context) => true;
        public IReadOnlyList<ProjectedGroup> Project(IReadOnlyList<TerminalEntry> entries, TerminalContext context,
            EntryAttributes attributes) => Array.Empty<ProjectedGroup>();
    }
}
