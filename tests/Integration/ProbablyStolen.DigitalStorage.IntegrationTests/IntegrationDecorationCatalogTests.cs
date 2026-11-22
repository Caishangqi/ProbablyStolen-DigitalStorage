using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Platform.Integration;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class IntegrationDecorationCatalogTests
{
    private static readonly IReadOnlyList<GameItem> SampleBatch = new GameItem[] { null!, null! };

    [Test]
    public void Evaluate_PassesReadOnlyBatchInOrder_AndPreservesResultIndices()
    {
        var first = new GameItem("first");
        var second = new GameItem("second");
        var catalog = new IntegrationDecorationCatalog();
        catalog.Activate("plugin", new[]
        {
            Contribution("plugin/z", items =>
            {
                Assert.That(items, Is.EqualTo(new[] { first, second }));
                Assert.Throws<NotSupportedException>((Action)(() => ((IList<GameItem>)items).Add(first)));
                return new int?[] { 0x112233, null };
            })
        });

        var result = catalog.Evaluate(new[] { first, second });
        Assert.That(result[0].Single().TintRgb, Is.EqualTo(0x112233));
        Assert.That(result[1], Is.Empty);
    }

    [Test]
    public void EvaluatePage_AlignsResultsAcrossMissingAndEmptySlotsAndUsesOnlyRepresentatives()
    {
        var representative = new TerminalEntry(new ItemKey("bottle"), 1, "Bottle", "sprite");
        var other = new TerminalEntry(ItemKey.Of("bottle", "variant", "other"), 1, "Bottle", "sprite");
        var missing = new TerminalEntry(new ItemKey("apple"), 1, "Apple", "sprite");
        var placeholder = new TerminalEntry(new ItemKey("lemon"), 0, "Lemon", "sprite");
        var hidden = new TerminalEntry(new ItemKey("pear"), 1, "Pear", "sprite");
        var merged = new DisplayEntry("merged", new[] { representative, other });
        var screen = new StorageScreen(columns: 3, rows: 1);
        screen.SetEntriesInOrder(new[] { DisplayEntry.Single(missing), merged, DisplayEntry.Single(placeholder), DisplayEntry.Single(hidden) });
        var calls = new List<ItemKey>();
        var sample = new GameItem("bottle");
        GameItem? Find(ItemKey key)
        {
            calls.Add(key);
            return key.Equals(representative.Key) ? sample : null;
        }

        var catalog = new IntegrationDecorationCatalog();
        var page = screen.GetPage();
        Assert.That(catalog.EvaluatePage(page, true, Find), Is.Null);
        catalog.Activate("plugin", new[] { Contribution("plugin/border", _ => new int?[] { 1 }) });
        Assert.That(catalog.EvaluatePage(page, false, Find), Is.Null);
        Assert.That(calls, Is.Empty);

        var result = catalog.EvaluatePage(page, true, Find)!;
        Assert.That(result.Select(slot => slot.Count), Is.EqualTo(new[] { 0, 1, 0 }));
        Assert.That(result[1].Single().TintRgb, Is.EqualTo(1));
        Assert.That(calls, Is.EqualTo(new[] { missing.Key, representative.Key }));
        Assert.That(screen.GetPage(), Is.SameAs(page));

        calls.Clear();
        screen.SetScroll(1);
        Assert.That(catalog.EvaluatePage(screen.GetPage(), true, Find), Is.Null);
        Assert.That(calls, Is.EqualTo(new[] { hidden.Key }));
    }

    [Test]
    public void Evaluate_OrdersPluginsAndContributionsByNamespacedId()
    {
        var catalog = new IntegrationDecorationCatalog();
        catalog.Activate("z", new[] { Contribution("z/b", _ => new int?[] { 2, 2 }) });
        catalog.Activate("a", new[]
        {
            Contribution("a/z", _ => new int?[] { 3, 3 }),
            Contribution("a/a", _ => new int?[] { 1, 1 })
        });

        var result = catalog.Evaluate(SampleBatch);

        foreach (var slot in result)
            Assert.That(slot.Select(decoration => decoration.TintRgb), Is.EqualTo(new[] { 1, 3, 2 }));
    }

    [TestCase("null", "InvalidOperationException")]
    [TestCase("count", "InvalidOperationException")]
    [TestCase("rgb", "InvalidOperationException")]
    public void Evaluate_MalformedOutputWithdrawsOnlyOwningPlugin(string malformed, string errorType)
    {
        var faults = new List<(string Plugin, string Contribution, string Reason, string? Error)>();
        var catalog = new IntegrationDecorationCatalog((plugin, id, reason, error) =>
            faults.Add((plugin, id, reason, error)));
        catalog.Activate("plugin_a", new[]
        {
            Contribution("plugin_a/a_valid", _ => new int?[] { 0x123456, 0x123456 }),
            Contribution("plugin_a/b_invalid", _ => malformed switch
            {
                "null" => null!,
                "count" => new int?[] { 0x123456 },
                _ => new int?[] { 0x123456, 0x1000000 }
            })
        });
        catalog.Activate("plugin_b", new[] { Contribution("plugin_b/valid", _ => new int?[] { 0xABCDEF, null }) });

        var result = catalog.Evaluate(SampleBatch);

        Assert.That(faults, Is.EqualTo(new[]
        {
            ("plugin_a", "plugin_a/b_invalid", "decoration_evaluate_failed", errorType)
        }));
        Assert.That(result[0].Select(item => item.TintRgb), Is.EqualTo(new[] { 0xABCDEF }));
        Assert.That(result[1], Is.Empty);
        Assert.That(catalog.Evaluate(SampleBatch)[0].Select(item => item.TintRgb), Is.EqualTo(new[] { 0xABCDEF }));
    }

    [Test]
    public void Withdraw_RemovesPluginContributionsAndActivationReplacesItsScope()
    {
        var catalog = new IntegrationDecorationCatalog();
        catalog.Activate("plugin", new[] { Contribution("plugin/old", _ => new int?[] { 1, 1 }) });
        catalog.Activate("plugin", new[] { Contribution("plugin/new", _ => new int?[] { 2, 2 }) });
        Assert.That(catalog.Evaluate(SampleBatch)[0].Single().TintRgb, Is.EqualTo(2));

        catalog.Withdraw("plugin");

        Assert.That(catalog.Evaluate(SampleBatch).All(slot => slot.Count == 0), Is.True);
    }

    private static IntegrationDecorationCatalog.Contribution Contribution(string id,
        Func<IReadOnlyList<GameItem>, IReadOnlyList<int?>> evaluate) =>
        new(id, "test_border", evaluate);
}
