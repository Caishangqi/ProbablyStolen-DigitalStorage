using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class StorageMenuTests
{
    [Test]
    public void BroadcastChanges_UpdatesAmountsRemovesUnmountedKeysAndRecoversOnReconnect()
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(true, ("apple", 5), ("banana", 2)));
        menu.BroadcastChanges(Snapshot(true, ("apple", 5), ("banana", 2)));
        var appleSerial = Entry(menu, "apple").Serial;
        Assert.That(menu.GetLinkState().IsConnected, Is.True);
        Assert.That(menu.GetEntries().Sum(entry => entry.Amount), Is.EqualTo(7));

        menu.BroadcastChanges(Snapshot(true, ("apple", 8), ("banana", 2)));
        Assert.That(Entry(menu, "apple").Serial, Is.EqualTo(appleSerial));
        Assert.That(menu.GetEntries().Single(entry => entry.Key.Identifier == "apple").Amount, Is.EqualTo(8));

        var appleKey = menu.GetEntries().Single(entry => entry.Key.Identifier == "apple").Key;
        menu.BroadcastChanges(Snapshot(true, ("banana", 2)));
        Assert.That(menu.GetStackBySerial(appleSerial), Is.Null);
        Assert.That(menu.GetRepository().GetAllEntries().Select(entry => entry.What!.GetPrimaryKey()),
            Is.EquivalentTo(new[] { "banana" }));

        menu.BroadcastChanges(new TerminalInventorySnapshot(false, System.Array.Empty<TerminalEntry>()));
        Assert.That(menu.GetLinkState().IsConnected, Is.False);
        Assert.That(menu.GetRepository().GetAllEntries(), Is.Empty);
        Assert.That(menu.GetSerial(appleKey), Is.Null);
        menu.BroadcastChanges(Snapshot(true, ("apple", 1)));
        Assert.That(menu.GetLinkState().IsConnected, Is.True);
        Assert.That(menu.GetEntries().Single().Amount, Is.EqualTo(1));
    }

    [Test]
    public void SearchDocument_SurvivesSnapshotQuantityDeltaAndPausedRemoval()
    {
        var document = new ProbablyStolen.DigitalStorage.Client.Terminal.Search.SearchDocument("apple", tagNames: new[] { "fruit" });
        var original = Item("apple", 2);
        var entry = new TerminalEntry(original.Key, 2, original.DisplayName, original.SpriteKey, document: document);
        var menu = new StorageMenu();
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[] { entry }));
        var serial = menu.GetEntries().Single().Serial;
        menu.GetRepository().HandleUpdate(false, new[] { new InventoryEntryUpdate(serial, null, 3) });
        Assert.That(menu.GetEntries().Single().Document, Is.SameAs(document));
        menu.SetPaused(true);
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, System.Array.Empty<TerminalEntry>()));
        Assert.That(menu.GetEntries().Single().Document, Is.SameAs(document));
        Assert.That(menu.GetEntries().Single().Amount, Is.Zero);
    }

    [Test]
    public void Reset_ClearsSessionRepositorySerialsAndPauseWithoutChangingStorageSnapshot()
    {
        var menu = new StorageMenu();
        var snapshot = Snapshot(true, ("apple", 5));
        menu.BroadcastChanges(snapshot);
        menu.SetPaused(true);

        menu.Reset();
        Assert.That(menu.GetLinkState().IsConnected, Is.False);
        Assert.That(menu.GetEntries(), Is.Empty);
        Assert.That(menu.GetRepository().GetAllEntries(), Is.Empty);
        Assert.That(snapshot.Entries.Single().Amount, Is.EqualTo(5));

        menu.BroadcastChanges(snapshot);
        Assert.That(menu.GetLinkState().IsConnected, Is.True);
        Assert.That(menu.GetEntries().Single().Amount, Is.EqualTo(5));
        Assert.That(Entry(menu, "apple").Serial, Is.EqualTo(1));
    }

    [Test]
    public void PausedView_KeepsRemovedSlotUntilResumeThenResortsCurrentKeys()
    {
        var menu = new StorageMenu();
        var screen = new StorageScreen(3, 1, 24);
        menu.BroadcastChanges(Snapshot(true, ("apple", 2), ("banana", 3)));
        screen.SetEntriesInOrder(menu.GetEntries());
        menu.SetPaused(true);

        var oldAppleSerial = menu.GetSerial(screen.GetPage().Slots[0].Entry!.Key)!.Value;
        menu.BroadcastChanges(Snapshot(true, ("banana", 4), ("carrot", 1)));
        screen.UpdateEntriesInOrder(menu.GetEntries());
        var paused = screen.GetPage();
        Assert.That(paused.Slots[0].Entry!.Key.Identifier, Is.EqualTo("apple"));
        Assert.That(paused.Slots[0].Entry!.Amount, Is.Zero);
        Assert.That(paused.Slots[1].Entry!.Key.Identifier, Is.EqualTo("banana"));
        Assert.That(paused.Slots[1].Entry!.Amount, Is.EqualTo(4));
        Assert.That(paused.Slots[2].Entry!.Key.Identifier, Is.EqualTo("carrot"));
        var removedSerial = menu.GetSerial(paused.Slots[0].Entry!.Key);
        Assert.That(removedSerial, Is.Null);
        var staleActionInvoked = false;
        var staleAction = menu.HandleInteraction(oldAppleSerial, 1, (_, _) =>
        {
            staleActionInvoked = true;
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 1, 1, "done");
        });
        Assert.That(staleAction.Status, Is.EqualTo(TerminalExtractionStatus.NoItems));
        Assert.That(staleActionInvoked, Is.False);

        Assert.That(menu.SetPaused(false), Is.True);
        screen.UpdateEntriesInOrder(menu.GetEntries());
        var resumed = screen.GetPage();
        Assert.That(resumed.Slots[0].Entry!.Key.Identifier, Is.EqualTo("banana"));
        Assert.That(resumed.Slots[1].Entry!.Key.Identifier, Is.EqualTo("carrot"));
        Assert.That(resumed.Slots[2].Entry, Is.Null);
    }

    [Test]
    public void HandleInteraction_UsesLinkSnapshotButDelegatesCommitToTheAuthoritativePath()
    {
        var menu = new StorageMenu();
        var item = Item("apple", 2);
        var invoked = false;
        menu.BroadcastChanges(Snapshot(true, ("apple", 2)));
        var liveSerial = menu.GetSerial(item.Key)!.Value;
        menu.BroadcastChanges(new TerminalInventorySnapshot(false, System.Array.Empty<TerminalEntry>()));
        var offline = menu.HandleInteraction(liveSerial, 1, (_, _) =>
        {
            invoked = true;
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 1, 1, "done");
        });
        Assert.That(offline.Status, Is.EqualTo(TerminalExtractionStatus.Offline));
        Assert.That(invoked, Is.False);

        menu.BroadcastChanges(Snapshot(true, ("apple", 2)));
        var reconnectedSerial = menu.GetSerial(item.Key)!.Value;
        Assert.That(reconnectedSerial, Is.Not.EqualTo(liveSerial));
        var success = menu.HandleInteraction(reconnectedSerial, 1, (_, _) =>
        {
            invoked = true;
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 1, 1, "done");
        });
        Assert.That(success.Status, Is.EqualTo(TerminalExtractionStatus.Success));
        Assert.That(invoked, Is.True);
    }

    [Test]
    public void HandleInteraction_ForwardsClickAndExplicitQuantitiesAndPreservesPartialResult()
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(true, ("apple", 6)));
        var serial = Entry(menu, "apple").Serial;
        var requested = new List<long>();

        var clicked = menu.HandleInteraction(serial, 1, (_, amount) =>
        {
            requested.Add(amount);
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, amount, amount, "done");
        });
        var explicitResult = menu.HandleInteraction(serial, 4, (_, amount) =>
        {
            requested.Add(amount);
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Partial, amount, 2, "partial");
        });

        Assert.That(requested, Is.EqualTo(new long[] { 1, 4 }));
        Assert.That(clicked.Succeeded, Is.True);
        Assert.That(explicitResult.Status, Is.EqualTo(TerminalExtractionStatus.Partial));
        Assert.That(explicitResult.IsPartial, Is.True);
        Assert.That(explicitResult.Succeeded, Is.False);
        Assert.That(explicitResult.ExtractedAmount, Is.EqualTo(2));
    }

    [Test]
    public void HandleInteraction_InvalidAmountDoesNotInvokeCommit()
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(true, ("apple", 2)));
        var serial = Entry(menu, "apple").Serial;
        var invoked = false;

        var result = menu.HandleInteraction(serial, 0, (_, _) =>
        {
            invoked = true;
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 0, 0, "unexpected");
        });

        Assert.That(result.Status, Is.EqualTo(TerminalExtractionStatus.InvalidAmount));
        Assert.That(result.MessageKey, Is.EqualTo("terminal_storage_invalid_amount"));
        Assert.That(invoked, Is.False);
    }


    [Test]
    public void RepositoryPause_KeepsExistingPositionsAndMarksRemovedEntriesEmptyUntilResume()
    {
        var repository = new ItemRepository();
        repository.HandleUpdate(true, new[] { Update(1, "apple", 2), Update(2, "banana", 3) });
        repository.SetPaused(true);
        repository.HandleUpdate(false, new[] { new InventoryEntryUpdate(1, null, 0) });
        Assert.That(repository.GetView()[0].What!.GetPrimaryKey(), Is.EqualTo("apple"));
        Assert.That(repository.GetView()[0].StoredAmount, Is.Zero);
        Assert.That(repository.GetAllEntries().Select(entry => entry.What!.GetPrimaryKey()),
            Is.EquivalentTo(new[] { "banana" }));
        repository.SetPaused(false);
        Assert.That(repository.GetView().Select(entry => entry.What!.GetPrimaryKey()), Is.EqualTo(new[] { "banana" }));
    }

    [Test]
    public void ReinsertWhilePaused_ReusesTheViewSlotWithANewSerialAndRejectsTheOldAction()
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(true, ("apple", 2), ("banana", 3)));
        var beforeRemoval = menu.GetEntries()[0];
        menu.SetPaused(true);
        menu.BroadcastChanges(Snapshot(true, ("banana", 3)));
        Assert.That(menu.GetEntries()[0].Serial, Is.EqualTo(beforeRemoval.Serial));
        Assert.That(menu.GetEntries()[0].Amount, Is.Zero);

        menu.BroadcastChanges(Snapshot(true, ("apple", 5), ("banana", 3)));
        var afterReinsert = menu.GetEntries()[0];
        Assert.That(menu.GetEntries(), Has.Count.EqualTo(2));
        Assert.That(afterReinsert.Key, Is.EqualTo(beforeRemoval.Key));
        Assert.That(afterReinsert.Serial, Is.Not.EqualTo(beforeRemoval.Serial));
        Assert.That(afterReinsert.Amount, Is.EqualTo(5));
        var invoked = false;
        var rejected = menu.HandleInteraction(beforeRemoval.Serial, 1, (_, _) =>
        {
            invoked = true;
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 1, 1, "done");
        });
        Assert.That(rejected.Status, Is.EqualTo(TerminalExtractionStatus.NoItems));
        Assert.That(invoked, Is.False);
    }

    [Test]
    public void RepositorySort_UsesCompleteStateRatherThanSourceEnumerationOrder()
    {
        var a = ItemKey.Of("tool", "condition", "a");
        var b = ItemKey.Of("tool", "condition", "b");
        var menu = new StorageMenu();
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[]
        {
            new TerminalEntry(b, 1, "Tool", "items|tool"),
            new TerminalEntry(a, 1, "Tool", "items|tool")
        }));
        Assert.That(menu.GetEntries().Select(entry => entry.Key), Is.EqualTo(new[] { a, b }));
    }

    [Test]
    public void RepositorySort_UsesOrdinalComponentNamesRatherThanTheirLength()
    {
        var first = ItemKey.Of("tool", "aa", "z");
        var second = ItemKey.Of("tool", "b", "a");
        var menu = new StorageMenu();
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[]
        {
            new TerminalEntry(second, 1, "Tool", "items|tool"),
            new TerminalEntry(first, 1, "Tool", "items|tool")
        }));

        Assert.That(menu.GetEntries().Select(entry => entry.Key), Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void UnavailableStorageSnapshot_DisconnectsWithUnavailableStatusAndNoEntries()
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(true, ("apple", 5)));
        Assert.That(menu.GetLinkState().IsConnected, Is.True);

        menu.BroadcastChanges(new TerminalInventorySnapshot(false,
            System.Array.Empty<TerminalEntry>(), storageUnavailable: true));

        Assert.That(menu.GetLinkState().IsConnected, Is.False);
        Assert.That(menu.GetLinkState().StatusDescription, Is.EqualTo("terminal_storage_unavailable"));
        Assert.That(menu.GetEntries(), Is.Empty);

        // A later unavailable read must not be presented as an empty online inventory.
        menu.BroadcastChanges(new TerminalInventorySnapshot(false,
            System.Array.Empty<TerminalEntry>(), storageUnavailable: true));
        Assert.That(menu.GetLinkState().IsConnected, Is.False);
        Assert.That(menu.GetLinkState().StatusDescription, Is.EqualTo("terminal_storage_unavailable"));
    }

    [TestCase(TerminalSortOrder.Name, TerminalSortDirection.Ascending, new[] { "apple", "banana", "cherry", "date" })]
    [TestCase(TerminalSortOrder.Name, TerminalSortDirection.Descending, new[] { "date", "cherry", "banana", "apple" })]
    [TestCase(TerminalSortOrder.Amount, TerminalSortDirection.Ascending, new[] { "banana", "date", "apple", "cherry" })]
    [TestCase(TerminalSortOrder.Amount, TerminalSortDirection.Descending, new[] { "cherry", "apple", "banana", "date" })]
    public void SetSort_OrdersByNameOrAmountWithStableTies(TerminalSortOrder order, TerminalSortDirection direction,
        string[] expected)
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(true, ("cherry", 9), ("apple", 5), ("date", 2), ("banana", 2)));

        menu.SetSort(order, direction);

        // Equal amounts (banana and date) keep name order in both directions.
        Assert.That(menu.GetEntries().Select(entry => entry.Key.Identifier), Is.EqualTo(expected));
    }

    [Test]
    public void SetSort_WhilePausedKeepsVisiblePositionsUntilResumed()
    {
        var menu = new StorageMenu();
        menu.BroadcastChanges(Snapshot(true, ("apple", 1), ("banana", 9)));
        menu.SetPaused(true);

        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        Assert.That(menu.GetEntries().Select(entry => entry.Key.Identifier), Is.EqualTo(new[] { "apple", "banana" }));

        menu.SetPaused(false);
        Assert.That(menu.GetEntries().Select(entry => entry.Key.Identifier), Is.EqualTo(new[] { "banana", "apple" }));
    }

    private static InventoryEntryUpdate Entry(StorageMenu menu, string id) =>
        menu.GetRepository().GetAllEntries().Single(entry => entry.What?.GetPrimaryKey() == id);

    private static InventoryEntryUpdate Update(long serial, string id, long amount) =>
        new(serial, new ItemKey(id), amount, displayName: id, spriteKey: "items|unknown");

    private static TerminalEntry Item(string id, long amount) =>
        new(new ItemKey(id), amount, id, "items|unknown");

    private static TerminalInventorySnapshot Snapshot(bool online, params (string Id, long Amount)[] items) =>
        new(online, items.Select(item => Item(item.Id, item.Amount)));
}
