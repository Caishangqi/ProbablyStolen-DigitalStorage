using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class TerminalDragBatchTests
{
    [Test]
    public void CreateDragBatch_UsesFinalSlotOrderAndCompleteGroupMemberOrder()
    {
        var menu = MenuWith(("bottle#half", "Bottle", 5), ("bottle#full", "Bottle", 4), ("apple", "Apple", 2));
        menu.SetProjection(ByIdentifier);
        menu.SetSort(TerminalSortOrder.Name, TerminalSortDirection.Ascending);
        var screen = ScreenFor(menu);
        var page = screen.GetPage();

        var batch = menu.CreateDragBatch(page, page, new[] { "id:bottle", "id:apple" }, page.PresentationRevision);

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.SelectedGroupIds, Is.EqualTo(new[] { "id:apple", "id:bottle" }));
        Assert.That(batch.Steps.Select(step => (step.GroupId, step.Key.Identifier, step.Pieces)), Is.EqualTo(new[]
        {
            ("id:apple", "apple", 2L),
            ("id:bottle", "bottle", 4L),
            ("id:bottle", "bottle", 5L)
        }));
        Assert.That(batch.RequestedPieces, Is.EqualTo(11));
    }

    [Test]
    public void CreateDragBatch_UsesGroupTotalForMergedPieceRequests()
    {
        var menu = MenuWith(("fillet#one", "Fish Fillet", 30), ("fillet#two", "Fish Fillet", 59));
        menu.SetProjection(ByIdentifier);
        var page = ScreenFor(menu).GetPage();

        var batch = menu.CreateDragBatch(page, page, new[] { "id:fillet" }, page.PresentationRevision);

        Assert.That(batch!.RequestedPieces, Is.EqualTo(89));
        Assert.That(batch.Steps.Select(step => step.Pieces), Is.EqualTo(new[] { 30L, 59L }));
    }

    [Test]
    public void CreateDragBatch_ClampsSingleGroupRequestAndUsesPlanOrder()
    {
        var menu = MenuWith(("bottle#full", "Bottle", 4), ("bottle#half", "Bottle", 5), ("bottle#empty", "Bottle", 2));
        menu.SetProjection(ByIdentifier);
        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        var page = ScreenFor(menu).GetPage();

        var batch = menu.CreateDragBatch(page, page, new[] { "id:bottle" }, page.PresentationRevision, 6);

        Assert.That(batch!.RequestedPieces, Is.EqualTo(6));
        Assert.That(batch.Steps.Select(step => step.Pieces), Is.EqualTo(new[] { 5L, 1L }));
        Assert.That(batch.Steps.Select(step => step.EntrySerial), Is.EqualTo(
            menu.GetEntries().Single(group => group.Id == "id:bottle").Members.Take(2).Select(member => member.Serial)));
    }

    [Test]
    public void CreateDragBatch_RejectsDuplicateHiddenZeroAndMultiGroupOverrideSelections()
    {
        var menu = MenuWith(("apple", "Apple", 2), ("banana", "Banana", 0), ("carrot", "Carrot", 1));
        var screen = new StorageScreen(1, 1);
        screen.SetEntriesInOrder(menu.GetEntries());
        var page = screen.GetPage();

        var firstId = page.Slots[0].Group!.Id;
        Assert.That(menu.CreateDragBatch(page, page, new[] { firstId, firstId }, 0), Is.Null);
        Assert.That(menu.CreateDragBatch(page, page, new[] { "k:missing" }, 0), Is.Null);
        Assert.That(menu.CreateDragBatch(page, page, new[] { "k:banana" }, 0), Is.Null);
        Assert.That(menu.CreateDragBatch(page, page, new[] { page.Slots[0].Group!.Id, "k:carrot" }, 0, 1), Is.Null);
    }

    [Test]
    public void CreateDragBatch_RejectsStalePagePipelineAndOrderedMemberChanges()
    {
        var menu = MenuWith(("bottle#one", "Bottle", 2), ("bottle#two", "Bottle", 2));
        menu.SetProjection(ByIdentifier);
        var screen = ScreenFor(menu);
        var shown = screen.GetPage();
        screen.SetPresentationRevision(1);
        var current = screen.GetPage();
        Assert.That(menu.CreateDragBatch(shown, current, new[] { "id:bottle" }, 1), Is.Null);

        var reordered = new StorageScreen(1, 1);
        reordered.SetEntriesInOrder(new[]
        {
            new DisplayEntry("id:bottle", new[] { Entry("bottle#two", "Bottle", 2, 2), Entry("bottle#one", "Bottle", 2, 1) })
        });
        var reorderedPage = reordered.GetPage();
        Assert.That(menu.CreateDragBatch(shown, reorderedPage, new[] { "id:bottle" }, 0), Is.Null);
    }

    [Test]
    public void CreateDragBatch_AllowsQuantityOnlyPageUpdateWithCurrentTotal()
    {
        var menu = MenuWith(("apple", "Apple", 2));
        var screen = ScreenFor(menu);
        var shown = screen.GetPage();
        menu.BroadcastChanges(Snapshot(("apple", "Apple", 5)));
        screen.UpdateEntriesInOrder(menu.GetEntries());
        var current = screen.GetPage();

        Assert.That(current.ViewRevision, Is.EqualTo(shown.ViewRevision));
        var batch = menu.CreateDragBatch(shown, current, new[] { shown.Slots[0].Group!.Id }, current.PresentationRevision, 8);
        Assert.That(batch!.RequestedPieces, Is.EqualTo(5));
    }

    [Test]
    public void CreateDragBatch_TakesSelectedGroupsOutsideTheShownRowsAfterAScroll()
    {
        var menu = MenuWith(("apple", "Apple", 1), ("bottle", "Bottle", 2), ("cherry", "Cherry", 3), ("date", "Date", 4));
        menu.SetProjection(ByIdentifier);
        menu.SetSort(TerminalSortOrder.Name, TerminalSortDirection.Ascending);
        var screen = ScreenFor(menu);
        var shown = screen.GetPage();
        screen.SetScroll(1);
        var current = screen.GetPage();

        Assert.That(current.Slots.Select(slot => slot.Group?.Id), Does.Not.Contain("id:apple"));
        var batch = menu.CreateDragBatch(shown, current, new[] { "id:date", "id:apple" }, current.PresentationRevision);

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.SelectedGroupIds, Is.EqualTo(new[] { "id:apple", "id:date" }));
        Assert.That(batch.RequestedPieces, Is.EqualTo(5));
        Assert.That(batch.ContentRevision, Is.EqualTo(current.ContentRevision));
    }

    [Test]
    public void CreateDragBatch_RejectsASelectionMadeBeforeTheFilterChanged()
    {
        var menu = MenuWith(("apple", "Apple", 1), ("bottle", "Bottle", 2), ("cherry", "Cherry", 3), ("date", "Date", 4));
        menu.SetProjection(ByIdentifier);
        var screen = ScreenFor(menu);
        var shown = screen.GetPage();
        screen.SetSearchText("e");
        var current = screen.GetPage();

        Assert.That(menu.CreateDragBatch(shown, current, new[] { "id:apple" }, current.PresentationRevision), Is.Null);
    }

    private static StorageScreen ScreenFor(StorageMenu menu)
    {
        var screen = new StorageScreen(3, 1);
        screen.SetEntriesInOrder(menu.GetEntries());
        return screen;
    }

    private static StorageMenu MenuWith(params (string Id, string Name, long Amount)[] items)
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(items));
        return menu;
    }

    private static TerminalInventorySnapshot Snapshot(params (string Id, string Name, long Amount)[] items) =>
        new(true, items.Select(item => Entry(item.Id, item.Name, item.Amount)).ToArray());

    private static TerminalEntry Entry(string id, string name, long amount, long serial = -1)
    {
        var parts = id.Split('#');
        var key = parts.Length == 1
            ? new ItemKey(id)
            : ItemKey.Of(parts[0], "variant", parts[1]);
        return new TerminalEntry(key, amount, name, "sprite", serial: serial);
    }

    private static IReadOnlyList<ProjectedGroup> ByIdentifier(IReadOnlyList<TerminalEntry> entries) =>
        entries.GroupBy(entry => entry.Key.Identifier)
            .Select(group => new ProjectedGroup("id:" + group.Key, group.ToArray())).ToArray();
}
