using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

public sealed class TerminalSearchCachingTests
{
    private static TerminalEntry Entry(string id, string name, string tag = "fruit", long amount = 1) =>
        new(new ItemKey(id), amount, name, "atlas|item", document: new SearchDocument(name, id, new[] { tag }));

    private static TerminalSearchService Service(Func<string, string, bool> matcher, SearchConfiguration? config = null)
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, options => options.Tags));
        registry.RegisterPrefix('#', "tags");
        registry.RegisterMatcher("test", matcher);
        return new TerminalSearchService(registry, config ?? new SearchConfiguration());
    }

    [Test]
    public void QueryAndConfigurationChanges_InvalidateResultsAndNoMatchState()
    {
        var calls = 0;
        var service = Service((_, _) => { calls++; return false; });
        var screen = new StorageScreen(search: service);
        screen.SetEntries(new[] { Entry("apple", "Apple") });
        screen.SetSearchText("#fruit");
        var matching = screen.GetPage();
        Assert.That(matching.State, Is.EqualTo(TerminalDisplayState.Ready));
        screen.SetSearchText("missing");
        var missing = screen.GetPage();
        Assert.That(missing.State, Is.EqualTo(TerminalDisplayState.Empty));
        var afterQuery = calls;
        Assert.That(screen.HasSearchResults("missing"), Is.False);
        Assert.That(screen.GetPage(), Is.SameAs(missing));
        Assert.That(calls, Is.EqualTo(afterQuery));
        screen.SetSearchText("#fruit");
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Ready));
        service.UpdateConfiguration(new SearchConfiguration(tags: SearchMode.Disabled));
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
        Assert.That(screen.HasSearchResults("#fruit"), Is.False);
        service.UpdateConfiguration(new SearchConfiguration());
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Ready));
    }

    [Test]
    public void ScrollAndResize_ReuseFilteredResultsAndClampPages()
    {
        var calls = 0;
        var service = Service((_, query) => { calls++; return query == "alias"; });
        var screen = new StorageScreen(columns: 1, rows: 1, search: service);
        screen.SetEntriesInOrder(Enumerable.Range(0, 10).Select(index => Entry("id" + index, "Name" + index)));
        screen.SetSearchText("alias");
        var first = screen.GetPage();
        var afterFilter = calls;
        screen.SetScroll(9);
        Assert.That(screen.GetPage().Slots[0].Entry!.Key.Identifier, Is.EqualTo("id9"));
        screen.SetRows(4);
        var resized = screen.GetPage();
        Assert.That(resized.ScrollRow, Is.EqualTo(6));
        Assert.That(resized.Slots.Count, Is.EqualTo(4));
        Assert.That(resized.Slots[3].Entry!.Key.Identifier, Is.EqualTo("id9"));
        screen.SetSearchText("alias");
        Assert.That(screen.GetPage(), Is.SameAs(resized), "Redundant input callbacks must not rebuild the page or reset scrolling.");
        Assert.That(calls, Is.EqualTo(afterFilter));
        screen.ScrollBy(-99);
        Assert.That(screen.GetPage().Slots[0].Entry, Is.SameAs(first.Slots[0].Entry));
        Assert.That(calls, Is.EqualTo(afterFilter));
    }

    [Test]
    public void SnapshotReplacement_RefreshesMetadataQuantitiesAndOrder()
    {
        var service = Service((_, _) => false);
        var screen = new StorageScreen(columns: 1, rows: 1, search: service);
        screen.SetSearchText("#fruit");
        screen.SetEntries(new[] { Entry("a", "Apple", amount: 1) });
        var old = screen.GetPage();
        screen.UpdateEntries(new[] { Entry("a", "Apple", amount: 17) });
        var updated = screen.GetPage();
        Assert.That(updated.Slots[0].QuantityBadge, Is.EqualTo("17"));
        Assert.That(old.Slots[0].QuantityBadge, Is.EqualTo("1"));
        screen.UpdateEntriesInOrder(new[] { Entry("a", "Apple", "metal", 17) });
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
        screen.SetEntriesInOrder(new[] { Entry("b", "Banana"), Entry("a", "Apple") });
        Assert.That(screen.GetPage().Slots[0].Entry!.Key.Identifier, Is.EqualTo("b"));
        screen.UpdateEntriesInOrder(new[] { Entry("a", "Apple"), Entry("b", "Banana") });
        Assert.That(screen.GetPage().Slots[0].Entry!.Key.Identifier, Is.EqualTo("a"));
        screen.SetOffline();
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Offline));
        Assert.That(screen.HasSearchResults("#fruit"), Is.False);
        screen.SetLoading();
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Loading));
        screen.SetEntries(new[] { Entry("a", "Apple") });
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Ready));
    }

    [Test]
    public void HighlightBeforeTypingCallback_SharesResultWithoutChangingCommittedQuery()
    {
        var calls = 0;
        var service = Service((_, query) => { calls++; return query == "alias"; });
        var screen = new StorageScreen(search: service);
        screen.SetEntries(new[] { Entry("a", "Apple") });
        Assert.That(screen.HasSearchResults("alias"), Is.True);
        var afterHighlight = calls;
        screen.SetSearchText("alias");
        Assert.That(screen.GetPage().SearchText, Is.EqualTo("alias"));
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Ready));
        Assert.That(calls, Is.EqualTo(afterHighlight));
        Assert.That(screen.HasSearchResults("unmatched"), Is.False);
        Assert.That(screen.GetPage().SearchText, Is.EqualTo("alias"));
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Ready));
    }

    [Test]
    public void FilterMemoization_DoesNotPersistExternalAddonStateAcrossSnapshots()
    {
        var matches = false;
        var calls = 0;
        var service = Service((_, _) => { calls++; return matches; });
        var screen = new StorageScreen(search: service);
        var entries = new[] { Entry("a", "Apple") };
        screen.SetEntries(entries);
        screen.SetSearchText("alias");
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
        matches = true;
        screen.UpdateEntries(entries);
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Ready));
        matches = false;
        service.UpdateConfiguration(service.Configuration);
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
        var standalone = new SearchDocument("Apple");
        matches = true;
        Assert.That(service.Matches(standalone, "alias"), Is.True);
        matches = false;
        Assert.That(service.Matches(standalone, "alias"), Is.False);
        Assert.That(calls, Is.GreaterThan(2));
    }

    [Test]
    public void PausedRemovalAndResume_InvalidateCachedPageWithoutLosingMetadata()
    {
        var service = Service((_, _) => false);
        var menu = new StorageMenu();
        var screen = new StorageScreen(search: service);
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[] { Entry("a", "Apple") }));
        screen.SetEntriesInOrder(menu.GetEntries());
        screen.SetSearchText("#fruit");
        Assert.That(screen.GetPage().Slots[0].Entry!.Amount, Is.EqualTo(1));
        menu.SetPaused(true);
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, Array.Empty<TerminalEntry>()));
        screen.UpdateEntriesInOrder(menu.GetEntries());
        Assert.That(screen.GetPage().Slots[0].Entry!.Amount, Is.Zero);
        menu.SetPaused(false);
        screen.UpdateEntriesInOrder(menu.GetEntries());
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
    }

    [Test]
    public void BoundedMemo_EvictsOldTermsAndStillCachesLaterRepeatedText()
    {
        var calls = 0;
        var service = Service((_, _) => { calls++; return false; });
        var screen = new StorageScreen(search: service);
        screen.SetSearchText("alias");
        screen.SetEntriesInOrder(Enumerable.Range(0, 4200).Select(index => Entry("id" + index, "Name" + index))
            .Concat(Enumerable.Range(0, 50).Select(index => Entry("tail" + index, "Repeated"))));
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
        Assert.That(calls, Is.EqualTo(4201), "Each unique source string is evaluated once, including repeats after eviction.");
    }

    [Test]
    public void SharedService_SeparateWindowsKeepIndependentCachedViews()
    {
        var service = Service((_, _) => false);
        var first = new StorageScreen(search: service);
        var second = new StorageScreen(search: service);
        var entries = new[] { Entry("a", "Apple"), Entry("b", "Banana", "metal") };
        first.SetEntries(entries);
        second.SetEntries(entries);
        first.SetSearchText("#fruit");
        second.SetSearchText("#metal");
        var fruit = first.GetPage();
        var metal = second.GetPage();
        Assert.That(first.GetPage(), Is.SameAs(fruit));
        Assert.That(second.GetPage(), Is.SameAs(metal));
        Assert.That(fruit.Slots[0].Entry!.Key.Identifier, Is.EqualTo("a"));
        Assert.That(metal.Slots[0].Entry!.Key.Identifier, Is.EqualTo("b"));
        service.UpdateConfiguration(new SearchConfiguration(tags: SearchMode.Disabled));
        Assert.That(first.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
        Assert.That(second.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
    }

    [TestCase("#fruit Apple")]
    [TestCase("#fruit -Banana")]
    [TestCase("#missing | Banana")]
    [TestCase("\"Apple juice\"")]
    [TestCase("Apple\\ ")]
    [TestCase("\"Apple ju")]
    [TestCase("#")]
    [TestCase("-")]
    public void CachedFilter_PreservesUncachedQuerySemantics(string query)
    {
        var service = Service((_, _) => false);
        var entries = new[] { Entry("a", "Apple juice"), Entry("b", "Banana", "metal") };
        var expected = entries.Where(entry => service.Matches(entry.Document, query)).Select(entry => entry.Key).ToArray();
        var screen = new StorageScreen(columns: 2, rows: 1, search: service);
        screen.SetEntriesInOrder(entries);
        screen.SetSearchText(query);
        var actual = screen.GetPage().Slots.Where(slot => slot.Entry != null).Select(slot => slot.Entry!.Key).ToArray();
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(screen.HasSearchResults(query), Is.EqualTo(expected.Length > 0));
    }

    [Test]
    public void RepeatedFrameReads_DoNotRepeatPinyinSearchForUnchangedQueryAndInventory()
    {
        using var addon = new PinyinSearchAddon(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Resources", "pinyin-dictionary.txt")), new SearchConfiguration());
        var calls = 0;
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, config => config.Tags));
        registry.RegisterPrefix('#', "tags");
        registry.RegisterMatcher("pinyin", (source, query) =>
        {
            calls++;
            return addon.Matches(source, query);
        });
        var service = new TerminalSearchService(registry, new SearchConfiguration());
        var screen = new StorageScreen(search: service);
        screen.SetEntries(Enumerable.Range(0, 256).Select(index => new TerminalEntry(new ItemKey("item" + index),
            index + 1, index % 2 == 0 ? "水杯" : "工具", "atlas|item",
            document: new SearchDocument(index % 2 == 0 ? "水杯" : "工具", tagNames: new[] { "日用品" }))));
        screen.SetSearchText("#riyong shu");
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var first = screen.GetPage();
        var initialCalls = calls;
        for (var frame = 0; frame < 120; frame++)
        {
            screen.GetPage();
            screen.GetTotalRows();
            screen.GetMaxScroll();
        }
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        TestContext.Out.WriteLine($"synthetic256-items120-frame-reads: {timer.Elapsed.TotalMilliseconds:F2}ms; allocatedBytes={allocated}; initialMatcherCalls={initialCalls}, totalMatcherCalls={calls}");
        Assert.That(first.State, Is.EqualTo(TerminalDisplayState.Ready));
        Assert.That(first.Slots[0].Entry!.DisplayName, Is.EqualTo("水杯"));
        for (var redraw = 0; redraw < 120; redraw++)
        {
            Assert.That(screen.GetPage(), Is.SameAs(first));
            Assert.That(screen.HasSearchResults("#riyong shu"), Is.True);
        }
        Assert.That(calls, Is.EqualTo(initialCalls), "Unchanged per-frame view reads must reuse the search result.");
    }
}
