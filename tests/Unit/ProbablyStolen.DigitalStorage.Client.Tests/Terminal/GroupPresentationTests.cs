using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class GroupPresentationTests
{
    [Test]
    public void GroupsSortByTotalSoAMergedStackMovesAsOne()
    {
        var menu = MenuWith(("fillet#1", "Fish Fillet (Large)", 30), ("fillet#2", "Fish Fillet (Large)", 59),
            ("apple", "Apple", 50));
        menu.SetProjection(ByIdentifier);
        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);

        var groups = menu.GetEntries();

        Assert.That(groups.Select(group => group.Total), Is.EqualTo(new long[] { 89, 50 }));
        Assert.That(groups[0].IsMixed, Is.True);
    }

    [Test]
    public void MembersFollowTheCurrentSortAndTheFirstIsTheRepresentative()
    {
        var menu = MenuWith(("bottle#full", "Bottle", 4), ("bottle#half", "Bottle", 5), ("bottle#empty", "Bottle", 2));
        menu.SetProjection(ByIdentifier);

        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        var descending = menu.GetEntries().Single();
        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Ascending);
        var ascending = menu.GetEntries().Single();

        Assert.That(descending.Members.Select(Variant), Is.EqualTo(new[] { "half", "full", "empty" }));
        Assert.That(descending.Representative.Amount, Is.EqualTo(5));
        Assert.That(ascending.Members.Select(Variant), Is.EqualTo(new[] { "empty", "full", "half" }));
        Assert.That(descending.Total, Is.EqualTo(11));
    }

    [Test]
    public void NameSortTiesFollowIdentityOrder()
    {
        var menu = MenuWith(("bottle#b", "Bottle", 1), ("bottle#a", "Bottle", 1));
        menu.SetProjection(ByIdentifier);
        Assert.That(menu.GetEntries().Single().Members.Select(Variant), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void IdentityProjection_KeepsOneEntryPerSlot()
    {
        var menu = MenuWith(("bottle#a", "Bottle", 1), ("bottle#b", "Bottle", 1));
        Assert.That(menu.GetEntries().Select(group => group.IsMixed), Is.EqualTo(new[] { false, false }));
    }

    [Test]
    public void PausedView_KeepsGroupPositionsAndResortsOnResume()
    {
        var menu = MenuWith(("a#1", "A", 1), ("b#1", "B", 5));
        menu.SetProjection(ByIdentifier);
        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        menu.SetPaused(true);
        menu.BroadcastChanges(Snapshot(("a#1", "A", 1), ("a#2", "A", 9), ("b#1", "B", 5)));

        Assert.That(menu.GetEntries().Select(group => group.Representative.Key.Identifier), Is.EqualTo(new[] { "b", "a" }));
        Assert.That(menu.GetEntries()[1].Total, Is.EqualTo(10));
        menu.SetPaused(false);
        Assert.That(menu.GetEntries().Select(group => group.Representative.Key.Identifier), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void ChangingTheProjectionWhilePaused_RebuildsWithoutDuplicates()
    {
        var menu = MenuWith(("a#1", "A", 1), ("a#2", "A", 2), ("b#1", "B", 5));
        menu.SetPaused(true);

        menu.SetProjection(ByIdentifier);

        Assert.That(menu.GetEntries().Select(group => group.Total), Is.EquivalentTo(new long[] { 3, 5 }));
        Assert.That(menu.GetEntries().All(group => group.Total > 0), Is.True);
    }

    [Test]
    public void SearchShowsAWholeGroupWhenOneMemberMatchesOnItsOwn()
    {
        var screen = new StorageScreen(4, 1, search: Search());
        var red = Entry("lamp#1", "Lamp", 1, "red");
        var round = Entry("lamp#2", "Lamp", 2, "round");
        screen.SetEntriesInOrder(new[] { new DisplayEntry("lamps", new[] { red, round }) });

        screen.SetSearchText("#red");
        var shown = screen.GetPage().Slots[0];
        Assert.That(shown.Group!.Members, Has.Count.EqualTo(2));
        Assert.That(shown.QuantityBadge, Is.EqualTo("3"));

        screen.SetSearchText("#red #round");
        Assert.That(screen.GetPage().Slots[0].Group, Is.Null, "Both terms must match one member.");
    }

    [Test]
    public void GroupInteraction_TakesMembersInOrderUntilTheRequestIsMet()
    {
        var menu = MenuWith(("bottle#full", "Bottle", 4), ("bottle#half", "Bottle", 5), ("bottle#empty", "Bottle", 2));
        menu.SetProjection(ByIdentifier);
        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        var asked = new List<(string Variant, long Pieces)>();

        var result = menu.HandleGroupInteraction(menu.GetEntries().Single(), TerminalClickAmount.For(TerminalClick.Half, 11),
            (member, pieces) =>
            {
                asked.Add((Variant(member), pieces));
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, pieces, pieces, "done");
            });

        Assert.That(asked, Is.EqualTo(new[] { ("half", 5L), ("full", 1L) }));
        Assert.That(result.ExtractedAmount, Is.EqualTo(6));
        Assert.That(result.Status, Is.EqualTo(TerminalExtractionStatus.Success));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void GroupInteraction_MemberFailureStopsAndPreservesActualMovedCount(int movedOnFailure)
    {
        var menu = MenuWith(("bottle#a", "Bottle", 2), ("bottle#b", "Bottle", 1), ("bottle#c", "Bottle", 1));
        menu.SetProjection(ByIdentifier);
        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        var calls = 0;

        var result = menu.HandleGroupInteraction(menu.GetEntries().Single(), 4, (_, pieces) =>
        {
            calls++;
            return calls == 1
                ? TerminalExtractionResult.Create(TerminalExtractionStatus.Success, pieces, pieces, "done")
                : TerminalExtractionResult.Create(TerminalExtractionStatus.Failed, pieces, movedOnFailure,
                    "terminal_storage_unavailable");
        });

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(result.Status, Is.EqualTo(TerminalExtractionStatus.Failed));
        Assert.That(result.MessageKey, Is.EqualTo("terminal_storage_unavailable"));
        Assert.That(result.RequestedAmount, Is.EqualTo(4));
        Assert.That(result.ExtractedAmount, Is.EqualTo(2 + movedOnFailure));
    }

    [Test]
    public void GroupInteraction_StopsAtTheFirstShortMemberAndKeepsWhatMoved()
    {
        var menu = MenuWith(("bottle#a", "Bottle", 2), ("bottle#b", "Bottle", 2));
        menu.SetProjection(ByIdentifier);
        var calls = 0;

        var result = menu.HandleGroupInteraction(menu.GetEntries().Single(), 4, (member, pieces) =>
        {
            calls++;
            var moved = calls == 1 ? pieces : 1;
            return TerminalExtractionResult.Create(moved == pieces ? TerminalExtractionStatus.Success
                : TerminalExtractionStatus.TargetRejected, pieces, moved, "x");
        });

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(result.Status, Is.EqualTo(TerminalExtractionStatus.Partial));
        Assert.That(result.ExtractedAmount, Is.EqualTo(3));
    }

    [Test]
    public void GroupInteraction_RefusesAGroupWhoseMembersChanged()
    {
        var menu = MenuWith(("bottle#a", "Bottle", 2));
        menu.SetProjection(ByIdentifier);
        var shown = menu.GetEntries().Single();
        menu.BroadcastChanges(Snapshot(("bottle#a", "Bottle", 2), ("bottle#b", "Bottle", 1)));
        var invoked = false;

        var result = menu.HandleGroupInteraction(shown, 1, (_, pieces) =>
        {
            invoked = true;
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, pieces, pieces, "x");
        });

        Assert.That(result.Status, Is.EqualTo(TerminalExtractionStatus.NoItems));
        Assert.That(invoked, Is.False);
    }

    [Test]
    public void ExtractionPlan_NeverAsksAMemberForMoreThanItHolds()
    {
        var group = new DisplayEntry("g", new[] { Entry("a#1", "A", 2), Entry("a#2", "A", 0), Entry("a#3", "A", 5) });
        var steps = GroupExtractionPlan.Steps(group, 4);
        Assert.That(steps.Select(step => (Variant(step.Member), step.Pieces)), Is.EqualTo(new[] { ("1", 2L), ("3", 2L) }));
    }

    [Test]
    public void DisplayEntry_RefusesMixedItemDefinitionsAndSumsUnits()
    {
        Assert.Throws<ArgumentException>((Action)(() => _ = new DisplayEntry("g", new[] { Entry("a#1", "A", 1), Entry("b#1", "B", 1) })));
        var group = new DisplayEntry("g", new[]
        {
            new TerminalEntry(Key("fuel#1"), 2, "Fuel", "s", totalUnits: 63), new TerminalEntry(Key("fuel#2"), 1, "Fuel", "s")
        });
        Assert.That(group.Total, Is.EqualTo(3));
        Assert.That(group.TotalUnits, Is.EqualTo(64));
    }

    private static IReadOnlyList<ProjectedGroup> ByIdentifier(IReadOnlyList<TerminalEntry> entries) =>
        entries.GroupBy(entry => entry.Key.Identifier).Select(group => new ProjectedGroup("id:" + group.Key, group.ToArray()))
            .ToArray();

    private static StorageMenu MenuWith(params (string Id, string Name, long Amount)[] items)
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(items));
        return menu;
    }

    private static TerminalInventorySnapshot Snapshot(params (string Id, string Name, long Amount)[] items) =>
        new(true, items.Select(item => Entry(item.Id, item.Name, item.Amount)).ToArray());

    private static TerminalEntry Entry(string id, string name, long amount, string? tag = null)
    {
        var key = Key(id);
        return new TerminalEntry(key, amount, name, "sprite",
            document: new SearchDocument(name, key.Identifier, tag == null ? Array.Empty<string>() : new[] { tag }));
    }

    private static ItemKey Key(string id)
    {
        var parts = id.Split('#');
        return new ItemKey(parts[0], new Dictionary<string, string> { ["variant"] = parts.Length > 1 ? parts[1] : "" });
    }

    private static string Variant(TerminalEntry entry) => entry.Key.Components.Single().Value;

    private static TerminalSearchService Search()
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, config => config.Tags));
        registry.RegisterPrefix('#', "tags");
        return new TerminalSearchService(registry, new SearchConfiguration());
    }
}
