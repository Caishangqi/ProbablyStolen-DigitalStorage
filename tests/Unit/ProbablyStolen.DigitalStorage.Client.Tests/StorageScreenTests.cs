using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class StorageScreenTests
{
    [Test]
    public void Page_AlwaysKeepsFixedSlotsAndFitsSpriteWithoutDistortion()
    {
        var screen = new StorageScreen(columns: 2, rows: 2, cellPixels: 32);
        screen.SetEntries(new[]
        {
            Entry("alpha", 4, "Alpha", "sprite_alpha", 2, 1),
            Entry("beta", 5, "Beta", "sprite_beta", 1, 2),
            Entry("gamma", 6, "Gamma", "sprite_gamma", 1, 1)
        });

        var page = screen.GetPage();

        Assert.That(page.State, Is.EqualTo(TerminalDisplayState.Ready));
        Assert.That(page.ScrollRow, Is.Zero);
        Assert.That(page.MaxScroll, Is.Zero);
        Assert.That(page.Slots, Has.Count.EqualTo(4));
        Assert.That(page.Slots[0].Entry!.DisplayName, Is.EqualTo("Alpha"));
        Assert.That(page.Slots[0].SpriteWidthPixels, Is.EqualTo(32));
        Assert.That(page.Slots[0].SpriteHeightPixels, Is.EqualTo(16));
        Assert.That(page.Slots[3].Entry, Is.Null);
    }

    [Test]
    public void Search_ResetsScrollAndFiltersDisplayAndIdentifier()
    {
        var screen = new StorageScreen(columns: 1, rows: 1);
        screen.SetEntries(new[]
        {
            Entry("apple", 1, "Apple", "apple"),
            Entry("banana", 2, "Banana", "banana"),
            Entry("copper_wire", 3, "Wire", "wire")
        });
        screen.ScrollBy(1);
        Assert.That(screen.GetPage().ScrollRow, Is.EqualTo(1));

        screen.SetSearchText("wire");
        var page = screen.GetPage();

        Assert.That(page.ScrollRow, Is.Zero);
        Assert.That(page.MaxScroll, Is.Zero);
        Assert.That(page.State, Is.EqualTo(TerminalDisplayState.Ready));
        Assert.That(page.Slots[0].Entry!.Key.Identifier, Is.EqualTo("copper_wire"));
    }

    [Test]
    public void Scroll_ClampsToLastFullRowsAndKeepsDeterministicStateAwareOrder()
    {
        var screen = new StorageScreen(columns: 1, rows: 2);
        screen.SetEntries(new[]
        {
            Entry("tool", 1, "Tool", "tool", state: "b"),
            Entry("tool", 1, "Tool", "tool", state: "a"),
            Entry("apple", 1, "Apple", "apple")
        });

        Assert.That(screen.GetPage().Slots[0].Entry!.DisplayName, Is.EqualTo("Apple"));
        screen.SetScroll(99);
        var last = screen.GetPage();

        // AE2 stops at totalRows - visibleRows, so the last view is full rather than one leftover row.
        Assert.That(last.ScrollRow, Is.EqualTo(1));
        Assert.That(last.MaxScroll, Is.EqualTo(1));
        Assert.That(last.Slots[0].Entry!.Key.Components[0].Value, Is.EqualTo("a"));
        Assert.That(last.Slots[1].Entry!.Key.Components[0].Value, Is.EqualTo("b"));
        screen.ScrollBy(-5);
        Assert.That(screen.GetPage().ScrollRow, Is.Zero);
    }

    [Test]
    public void ScrollRange_CountsWholeRowsAndIsZeroWhenEverythingFits()
    {
        var screen = new StorageScreen(columns: 3, rows: 2);
        screen.SetEntries(Enumerable.Range(0, 6).Select(index => Entry($"item{index}", 1, $"Item {index}", "s")));
        Assert.That(screen.GetTotalRows(), Is.EqualTo(2));
        Assert.That(screen.GetMaxScroll(), Is.Zero);
        screen.ScrollBy(1);
        Assert.That(screen.ScrollRow, Is.Zero);

        screen.SetEntries(Enumerable.Range(0, 7).Select(index => Entry($"item{index}", 1, $"Item {index}", "s")));
        Assert.That(screen.GetTotalRows(), Is.EqualTo(3));
        Assert.That(screen.GetMaxScroll(), Is.EqualTo(1));
        screen.ScrollBy(1);
        var page = screen.GetPage();
        Assert.That(page.Slots[0].Entry!.DisplayName, Is.EqualTo("Item 3"));
        Assert.That(page.Slots[3].Entry!.DisplayName, Is.EqualTo("Item 6"));
        Assert.That(page.Slots[4].Entry, Is.Null);
    }

    [TestCase(2, 1)]
    [TestCase(5, 1)]
    [TestCase(6, 1)]
    [TestCase(11, 1)]
    [TestCase(12, 2)]
    [TestCase(19, 3)]
    public void PageSize_FollowsAe2RowsDividedBySix(int rows, int expected)
    {
        Assert.That(new StorageScreen(columns: 7, rows: rows).PageSize, Is.EqualTo(expected));
    }

    [Test]
    public void SetRows_ChangesSlotCountAndClampsScroll()
    {
        var screen = new StorageScreen(columns: 1, rows: 1);
        screen.SetEntries(Enumerable.Range(0, 5).Select(index => Entry($"item{index}", 1, $"Item {index}", "s")));
        screen.SetScroll(4);
        Assert.That(screen.ScrollRow, Is.EqualTo(4));

        screen.SetRows(3);
        var page = screen.GetPage();
        Assert.That(page.Slots, Has.Count.EqualTo(3));
        Assert.That(page.ScrollRow, Is.EqualTo(2));
        Assert.That(page.Slots[2].Entry!.DisplayName, Is.EqualTo("Item 4"));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => screen.SetRows(0)));
    }

    [Test]
    public void SetEntries_UsesOrdinalComponentPairsInsteadOfEncodedLength()
    {
        var screen = new StorageScreen(columns: 2, rows: 1);
        var alphabeticFirst = ItemKey.Of("tool", "aa", "z");
        var alphabeticSecond = ItemKey.Of("tool", "b", "a");
        screen.SetEntries(new[]
        {
            new TerminalEntry(alphabeticSecond, 1, "Tool", "tool"),
            new TerminalEntry(alphabeticFirst, 1, "Tool", "tool")
        });

        Assert.That(screen.GetPage().Slots.Select(slot => slot.Entry!.Key),
            Is.EqualTo(new[] { alphabeticFirst, alphabeticSecond }));
    }

    [Test]
    public void IdentitySortKey_OrdinalComparisonMatchesCompleteIdentityAcrossCultures()
    {
        var keys = new[]
        {
            new ItemKey("tool"),
            new ItemKey("tool\0"),
            ItemKey.Of("tool", "aa", "z"),
            ItemKey.Of("tool", "b", "a"),
            ItemKey.Of("tool", "a\0", "|:1"),
            ItemKey.Of("tool", "a", ""),
            ItemKey.Of("tool", "a", "\0"),
            ItemKey.Of("tool", "a", "\ud800"),
            ItemKey.Of("tool", "a", "\ud83d\ude00"),
            ItemKey.Of("tool", "a", "\uffff"),
            new ItemKey("tool", new[]
            {
                new KeyValuePair<string, string>("a", ""),
                new KeyValuePair<string, string>("z", "0")
            }),
            new ItemKey("\u0130")
        };
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (var left in keys)
                {
                    foreach (var right in keys)
                    {
                        var leftEntry = new TerminalEntry(left, 1, "item", "sprite");
                        var rightEntry = new TerminalEntry(right, 1, "item", "sprite");
                        Assert.That(System.Math.Sign(string.CompareOrdinal(leftEntry.GetIdentitySortKey(), rightEntry.GetIdentitySortKey())),
                            Is.EqualTo(System.Math.Sign(left.CompareTo(right))));
                    }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void IdentitySortKey_MixedLegacyAndVersionedKeysMatchesIdentityComparer()
    {
        var leaf = Convert.FromHexString("44534944000000010a01010000000161000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
        var withEmptyName = leaf.Take(16).Concat(new byte[] { 1, 0, 0, 0, 0 }).Concat(leaf.Skip(17)).ToArray();
        var keys = new[] { ItemKey.FromCanonical("a", withEmptyName), ItemKey.Of("a", "x", "1"),
            ItemKey.FromCanonical("a", leaf), new ItemKey("a"), new ItemKey("b") };
        foreach (var left in keys)
            foreach (var right in keys)
            {
                var leftSort = new TerminalEntry(left, 1, "item", "sprite").GetIdentitySortKey();
                var rightSort = new TerminalEntry(right, 1, "item", "sprite").GetIdentitySortKey();
                Assert.That(Math.Sign(string.CompareOrdinal(leftSort, rightSort)), Is.EqualTo(Math.Sign(left.CompareTo(right))));
            }
    }

    [Test]
    public void LiveInventoryUpdate_PreservesSearchAndScrollUntilEntriesRequireClamping()
    {
        var screen = new StorageScreen(columns: 1, rows: 1);
        screen.SetEntries(new[]
        {
            Entry("apple", 1, "Apple", "apple"),
            Entry("banana", 2, "Banana", "banana"),
            Entry("copper_wire", 3, "Wire", "wire")
        });
        screen.SetSearchText("a");
        screen.SetScroll(1);

        screen.UpdateEntries(new[]
        {
            Entry("apple", 1, "Apple", "apple"),
            Entry("banana", 4, "Banana", "banana"),
            Entry("copper_wire", 3, "Wire", "wire")
        });

        var page = screen.GetPage();
        Assert.That(page.SearchText, Is.EqualTo("a"));
        Assert.That(page.ScrollRow, Is.EqualTo(1));
        Assert.That(page.Slots[0].Entry!.Key.Identifier, Is.EqualTo("banana"));
        Assert.That(page.Slots[0].Entry!.Amount, Is.EqualTo(4));

        screen.UpdateEntries(new[] { Entry("apple", 2, "Apple", "apple") });
        page = screen.GetPage();
        Assert.That(page.ScrollRow, Is.Zero);
        Assert.That(page.State, Is.EqualTo(TerminalDisplayState.Ready));
        Assert.That(page.Slots[0].Entry!.Amount, Is.EqualTo(2));
        screen.SetSearchText("missing");
        screen.UpdateEntries(new[] { Entry("apple", 3, "Apple", "apple") });
        page = screen.GetPage();
        Assert.That(page.State, Is.EqualTo(TerminalDisplayState.Empty));
        Assert.That(page.Slots.All(slot => slot.Entry == null), Is.True);
    }

    [Test]
    public void OrderedInventoryUpdate_PreservesCallerOrderSearchAndValidScroll()
    {
        var screen = new StorageScreen(columns: 1, rows: 1);
        screen.SetEntriesInOrder(new[]
        {
            Entry("zebra", 1, "Zebra", "zebra"),
            Entry("apple", 2, "Apple", "apple"),
            Entry("banana", 3, "Banana", "banana")
        });
        screen.SetSearchText(string.Empty);
        screen.SetScroll(2);
        screen.UpdateEntriesInOrder(new[]
        {
            Entry("zebra", 4, "Zebra", "zebra"),
            Entry("apple", 2, "Apple", "apple"),
            Entry("banana", 3, "Banana", "banana")
        });

        var page = screen.GetPage();
        Assert.That(page.SearchText, Is.Empty);
        Assert.That(page.ScrollRow, Is.EqualTo(2));
        Assert.That(page.Slots[0].Entry!.Key.Identifier, Is.EqualTo("banana"));
        screen.SetLoading();
        page = screen.GetPage();
        Assert.That(page.SearchText, Is.Empty);
        Assert.That(page.ScrollRow, Is.Zero);
        Assert.That(page.State, Is.EqualTo(TerminalDisplayState.Loading));
    }

    [Test]
    public void StateTransitions_ClearStaleItems()
    {
        var screen = new StorageScreen(columns: 2, rows: 1);
        screen.SetEntries(new[] { Entry("apple", 1, "Apple", "apple") });
        screen.SetSearchText("missing");
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Empty));

        screen.SetLoading();
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Loading));
        Assert.That(screen.GetPage().SearchText, Is.EqualTo("missing"));
        Assert.That(screen.GetPage().ScrollRow, Is.Zero);
        Assert.That(screen.GetPage().Slots[0].Entry, Is.Null);
        screen.SetOffline();
        Assert.That(screen.GetPage().State, Is.EqualTo(TerminalDisplayState.Offline));
    }

    [TestCase(0, "0")]
    [TestCase(9999, "9999")]
    [TestCase(10000, "10k")]
    [TestCase(12500, "12.5k")]
    [TestCase(1000000, "1m")]
    public void QuantityBadge_UsesCompactTextWithoutChangingSourceCount(long count, string expected)
    {
        Assert.That(TerminalQuantityFormatter.Format(count), Is.EqualTo(expected));
    }

    private static TerminalEntry Entry(string id, long amount, string name, string sprite,
        int width = 1, int height = 1, string? state = null)
    {
        var key = state == null
            ? new ItemKey(id)
            : ItemKey.Of(id, "state", state);
        return new TerminalEntry(key, amount, name, sprite, width, height);
    }
}
