using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

public sealed class TerminalSearchPresentationTests
{
    private static TerminalEntry Entry(string id, string name, string tag) =>
        new(new ItemKey(id), 1, name, "atlas|item", document: new SearchDocument(name, id, new[] { tag }));

    private static SearchMatcherRegistry Registry()
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, config => config.Tags));
        registry.RegisterPrefix('#', "tags");
        return registry;
    }

    [Test]
    public void TypingAndConfigurationChanges_UseCapturedPicturesAndCurrentDetachedSnapshot()
    {
        var captures = 0;
        var allowCapture = true;
        var cache = new TerminalSnapshotCache<string>(entry =>
        {
            Assert.That(allowCapture, Is.True, "View-only changes must not call the native capture port.");
            captures++;
            return entry.Key.Identifier + " picture";
        });
        var service = new TerminalSearchService(Registry(), new SearchConfiguration());
        var screen = new StorageScreen(2, 1, search: service);
        var entries = new[] { Entry("apple", "Apple", "fruit"), Entry("iron", "Iron", "metal") };
        cache.Accept(entries);
        screen.SetEntries(entries);
        allowCapture = false;
        screen.SetSearchText("#fruit");
        Assert.That(screen.GetPage().Slots[0].Entry!.Key.Identifier, Is.EqualTo("apple"));
        Assert.That(cache.Get(screen.GetPage().Slots[0].Entry!.Key), Is.EqualTo("apple picture"));
        screen.SetSearchText("#metal");
        Assert.That(cache.Get(screen.GetPage().Slots[0].Entry!.Key), Is.EqualTo("iron picture"));
        Assert.That(cache.Get(new ItemKey("missing")), Is.Null);
        var revision = service.ConfigurationRevision;
        service.UpdateConfiguration(new SearchConfiguration(tags: SearchMode.Disabled));
        Assert.That(service.ConfigurationRevision, Is.GreaterThan(revision));
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
        Assert.That(service.GetColorSpans("#metal", true).Any(span => span.Kind == SearchColorKind.Prefix), Is.False);
        Assert.That(captures, Is.EqualTo(2));
        cache.Clear();
        Assert.That(cache.Get(entries[0].Key), Is.Null);
    }

    [Test]
    public void ExplicitAddon_CapturesNamedFieldsOnceAndSealsRegistration()
    {
        var registry = Registry();
        var captures = 0;
        var terms = new List<string> { "alchemy" };
        registry.RegisterDocumentField("craft", document =>
        {
            captures++;
            Assert.That(document.Identifier, Is.EqualTo("potion"));
            return terms;
        }, _ => SearchMode.RequirePrefix);
        registry.RegisterPrefix('%', "craft");
        registry.RegisterMatcher("alias", (source, query) => source == "Potion" && query == "elixir");
        var service = new TerminalSearchService(registry, new SearchConfiguration(enabled: false));
        var document = service.PrepareDocument(new SearchDocument("Potion", "potion"));
        terms.Clear();
        Assert.That(service.Matches(document, "%alchemy"), Is.True);
        Assert.That(service.Matches(document, "elixir"), Is.True);
        Assert.That(service.Matches(document, "alchemy"), Is.False);
        Assert.That(captures, Is.EqualTo(1));
        Action late = () => registry.RegisterMatcher("late", (_, _) => true);
        Assert.Throws<InvalidOperationException>(late);
        var other = Registry();
        other.RegisterDocumentField("craft", _ => Array.Empty<string>(), _ => SearchMode.Enabled);
        Action duplicate = () => other.RegisterDocumentField("craft", _ => Array.Empty<string>(), _ => SearchMode.Enabled);
        Assert.Throws<InvalidOperationException>(duplicate);
    }

    [Test]
    public void MenuAndPausedDeltas_KeepMetadataSearchableAcrossScreenUpdates()
    {
        var service = new TerminalSearchService(Registry(), new SearchConfiguration());
        var menu = new StorageMenu();
        var screen = new StorageScreen(2, 1, search: service);
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[] { Entry("apple", "Apple", "fruit") }));
        screen.SetEntriesInOrder(menu.GetEntries());
        screen.SetSearchText("#fruit");
        var serial = menu.GetEntries().Single().Serial;
        menu.GetRepository().HandleUpdate(false, new[] { new InventoryEntryUpdate(serial, null, 3) });
        screen.UpdateEntriesInOrder(menu.GetEntries());
        Assert.That(screen.GetPage().Slots[0].Entry!.Amount, Is.EqualTo(3));
        menu.SetPaused(true);
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, Array.Empty<TerminalEntry>()));
        screen.UpdateEntriesInOrder(menu.GetEntries());
        Assert.That(screen.GetPage().Slots[0].Entry!.Amount, Is.Zero);
        menu.SetPaused(false);
        screen.UpdateEntriesInOrder(menu.GetEntries());
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));
    }

    [Test]
    public void RawQuery_PreservesEscapedTrailingSpaceAndIncompleteQuotes()
    {
        var service = new TerminalSearchService(Registry(), new SearchConfiguration());
        var screen = new StorageScreen(2, 1, search: service);
        screen.SetEntries(new[] { Entry("a", "foo bar", "fruit"), Entry("b", "foo", "metal") });
        screen.SetSearchText("foo\\ ");
        Assert.That(screen.GetPage().SearchText, Is.EqualTo("foo\\ "));
        Assert.That(screen.GetPage().Slots[0].Entry!.Key.Identifier, Is.EqualTo("a"));
        Assert.That(screen.GetPage().Slots[1].Entry, Is.Null);
        screen.SetSearchText("\"foo b");
        Assert.That(screen.GetPage().Slots[0].Entry!.Key.Identifier, Is.EqualTo("a"));
        Assert.That(service.Matches(new SearchDocument("slash\\"), "slash\\"), Is.True);
        Assert.That(service.Matches(new SearchDocument("#literal"), "#literal"), Is.False);
        service.UpdateConfiguration(new SearchConfiguration(tags: SearchMode.Disabled));
        Assert.That(service.Matches(new SearchDocument("#literal"), "#literal"), Is.True);
    }

    [TestCase(PinyinKeyboard.Xiaohe)]
    [TestCase(PinyinKeyboard.Ziranma)]
    [TestCase(PinyinKeyboard.Guobiao)]
    [TestCase(PinyinKeyboard.PinyinPp)]
    [TestCase(PinyinKeyboard.Ziguang)]
    public void DoublePinyin_MatchesUppercaseAndBothFuzzyDirections(PinyinKeyboard keyboard)
    {
        using var addon = new PinyinSearchAddon("路: lu4\n女: nv3\n", new SearchConfiguration(keyboard: keyboard, u2v: true));
        Assert.That(addon.Matches("路", "LV4"), Is.True);
        Assert.That(addon.Matches("女", "NU3"), Is.True);
    }

    [TestCase(PinyinKeyboard.Quanpin, "ang2")]
    [TestCase(PinyinKeyboard.Daqian, ";6")]
    [TestCase(PinyinKeyboard.Xiaohe, "ah2")]
    [TestCase(PinyinKeyboard.Ziranma, "ah2")]
    [TestCase(PinyinKeyboard.Sougou, "ah2")]
    [TestCase(PinyinKeyboard.Guobiao, "ag2")]
    [TestCase(PinyinKeyboard.Microsoft, "ah2")]
    [TestCase(PinyinKeyboard.PinyinPp, "ag2")]
    [TestCase(PinyinKeyboard.Ziguang, "as2")]
    public void ZeroInitialSyllables_RetainToneAndKeyboardMapping(PinyinKeyboard keyboard, string query)
    {
        using var addon = new PinyinSearchAddon("昂: ang2\n", new SearchConfiguration(keyboard: keyboard));
        Assert.That(addon.Matches("昂", query.ToUpperInvariant()), Is.True);
        Assert.That(addon.Matches("昂", query + "x"), Is.False);
    }

    [Test]
    public void LongMismatch_AfterTransitionCacheEviction_ReusesNewRepeatedCharacter()
    {
        using var addon = new PinyinSearchAddon("测: ce4\n", new SearchConfiguration());
        var distinct = new string(Enumerable.Range(0x100, 64).Select(value => (char)value).ToArray());
        Assert.That(addon.Matches(distinct + new string('测', 5000), new string('c', 4999) + "x"), Is.False);
    }

    [Test]
    public void PictureFailure_FallsBackPerEntryAndAcceptsRemainingInventory()
    {
        var failures = 0;
        var cache = new TerminalSnapshotCache<string>(entry =>
        {
            if (entry.Key.Identifier == "broken") throw new InvalidOperationException("synthetic picture failure");
            return "picture";
        }, (_, error) =>
        {
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            failures++;
            return "fallback";
        });
        var entries = new[] { Entry("broken", "Broken", "a"), Entry("good", "Good", "b") };
        var snapshot = new TerminalInventorySnapshot(true, entries);
        var monitor = new TerminalInventoryMonitor();
        var menu = new StorageMenu();
        monitor.ApplySnapshot(snapshot, () =>
        {
            cache.Accept(entries);
            menu.BroadcastChanges(snapshot);
        });
        Assert.That(cache.Get(entries[0].Key), Is.EqualTo("fallback"));
        Assert.That(cache.Get(entries[1].Key), Is.EqualTo("picture"));
        Assert.That(menu.GetEntries().Count, Is.EqualTo(2));
        Assert.That(monitor.HasChanged(snapshot), Is.False);
        Assert.That(failures, Is.EqualTo(1));
    }

    [Test]
    public void FailedDisplayUpdate_RetainsOldBaselineAndRequestsRetry()
    {
        var monitor = new TerminalInventoryMonitor();
        var old = new TerminalInventorySnapshot(true, new[] { Entry("old", "Old", "a") });
        var next = new TerminalInventorySnapshot(true, new[] { Entry("next", "Next", "b") });
        monitor.Accept(old);
        monitor.MarkDirty();
        Assert.That(monitor.BeginRefresh(0, true, false), Is.EqualTo(TerminalRefreshReason.Dirty));
        Action apply = () => monitor.ApplySnapshot(next, () => throw new InvalidOperationException("synthetic display failure"));
        Assert.Throws<InvalidOperationException>(apply);
        Assert.That(monitor.HasChanged(old), Is.False);
        Assert.That(monitor.HasChanged(next), Is.True);
        Assert.That(monitor.BeginRefresh(1, true, false), Is.EqualTo(TerminalRefreshReason.Dirty));
        monitor.ApplySnapshot(next, () => { });
        Assert.That(monitor.HasChanged(next), Is.False);
        Assert.That(monitor.BeginRefresh(2, true, false), Is.EqualTo(TerminalRefreshReason.None));
    }

    [TestCase(63)]
    [TestCase(64)]
    [TestCase(65)]
    [TestCase(128)]
    public void PinyinReachability_CrossesBitsetWordsWithoutWrapping(int length)
    {
        using var addon = new PinyinSearchAddon("测: ce4\n", new SearchConfiguration());
        var source = new string('测', length);
        Assert.That(addon.Matches(source, new string('c', length)), Is.True);
        Assert.That(addon.Matches(source, new string('c', length) + "x"), Is.False);
        Assert.That(addon.Matches("测试Ab", "ce试AB"), Is.True);
    }
}
