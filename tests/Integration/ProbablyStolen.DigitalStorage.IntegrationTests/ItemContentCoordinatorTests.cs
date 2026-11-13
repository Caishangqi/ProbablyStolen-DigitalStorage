using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Content;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
internal sealed class ItemContentCoordinatorTests
{
    private sealed class Item
    {
        internal int Value;
        internal bool Fail;
    }

    [Test]
    public void LoadBatch_TracksCandidatesAndDeduplicatesByReference()
    {
        var notifications = 0;
        var calls = new List<Item>();
        var coordinator = new ItemContentCoordinator<Item>((item, _) =>
        {
            calls.Add(item);
            item.Value = 100;
            return ItemContentResult.Changed;
        }, () => notifications++, _ => { });
        var candidate = new Item();
        var enumerated = new Item();
        coordinator.Track(candidate);
        coordinator.Track(candidate);
        Assert.That(candidate.Value, Is.Zero);
        var result = coordinator.ApplyTracked(false, new[] { candidate, enumerated, enumerated });
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(result.Changed, Is.EqualTo(2));
        Assert.That(notifications, Is.EqualTo(1));
    }

    [Test]
    public void LoadBatch_ContinuesAfterFailureAndReportsPartialResult()
    {
        var notifications = 0;
        var errors = 0;
        var coordinator = new ItemContentCoordinator<Item>((item, _) => item.Fail
            ? throw new InvalidOperationException() : ItemContentResult.Changed,
            () => notifications++, _ => errors++);
        var result = coordinator.Apply(new[] { new Item { Fail = true }, new Item() }, false);
        Assert.That(result.Failed, Is.EqualTo(1));
        Assert.That(result.Changed, Is.EqualTo(1));
        Assert.That(errors, Is.EqualTo(1));
        Assert.That(notifications, Is.EqualTo(1));
    }

    [Test]
    public void Locale_UsesTextOnlyAndResetAndDisposeDropPreviousSession()
    {
        var calls = new List<(Item, bool)>();
        var coordinator = new ItemContentCoordinator<Item>((item, textOnly) =>
        {
            calls.Add((item, textOnly));
            return ItemContentResult.Unchanged;
        }, () => Assert.Fail("Unexpected change"), _ => Assert.Fail("Unexpected failure"));
        var old = new Item();
        coordinator.Track(old);
        coordinator.ApplyTracked(true);
        Assert.That(calls, Is.EqualTo(new[] { (old, true) }));
        coordinator.Reset();
        calls.Clear();
        coordinator.ApplyTracked(false);
        Assert.That(calls, Is.Empty);
        coordinator.Track(new Item());
        coordinator.Dispose();
        coordinator.ApplyTracked(false);
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public void Tracking_DoesNotKeepItemAlive()
    {
        var coordinator = new ItemContentCoordinator<Item>((_, _) => ItemContentResult.Unchanged, () => { }, _ => { });
        var weak = TrackTemporary(coordinator);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(weak.TryGetTarget(out _), Is.False);
        Assert.That(coordinator.ApplyTracked(false).Unchanged, Is.Zero);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Item> TrackTemporary(ItemContentCoordinator<Item> coordinator)
    {
        var item = new Item();
        coordinator.Track(item);
        return new WeakReference<Item>(item);
    }

    [Test]
    public void Tracking_PrunesDeadWeakReferencesDuringItemChurn()
    {
        var coordinator = new ItemContentCoordinator<Item>((_, _) => ItemContentResult.Unchanged, () => { }, _ => { });
        var transient = TrackManyTemporarily(coordinator, 64);
        Assert.That(transient, Has.Count.EqualTo(64));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var live = new List<Item>();
        for (var index = 0; index < 64; index++)
        {
            var item = new Item();
            live.Add(item);
            coordinator.Track(item);
        }

        Assert.That(coordinator.TrackedCount, Is.EqualTo(64), "Dead weak-reference wrappers are pruned at the bounded threshold.");
    }

    [Test]
    public void ApplyTracked_ResetsPruneThresholdAfterCompactingDeadReferences()
    {
        var coordinator = new ItemContentCoordinator<Item>((_, _) => ItemContentResult.Unchanged, () => { }, _ => { });
        var live = TrackManyKeepingFirst(coordinator, 1024, 1024);
        live.RemoveRange(64, live.Count - 64);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        coordinator.ApplyTracked(false);
        Assert.That(coordinator.TrackedCount, Is.EqualTo(64));

        TrackManyTemporarily(coordinator, 63);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        coordinator.Track(new Item());

        Assert.That(coordinator.TrackedCount, Is.EqualTo(65),
            "A compacted tracked set should prune at its new size rather than the previous large threshold.");
        GC.KeepAlive(live);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<Item> TrackManyKeepingFirst(ItemContentCoordinator<Item> coordinator, int count, int keepCount)
    {
        var live = new List<Item>(keepCount);
        for (var index = 0; index < count; index++)
        {
            var item = new Item();
            if (index < keepCount) live.Add(item);
            coordinator.Track(item);
        }
        return live;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<WeakReference<Item>> TrackManyTemporarily(ItemContentCoordinator<Item> coordinator, int count)
    {
        var weak = new List<WeakReference<Item>>(count);
        for (var index = 0; index < count; index++)
        {
            var item = new Item();
            coordinator.Track(item);
            weak.Add(new WeakReference<Item>(item));
        }
        return weak;
    }

    [TestCase("digital_storage.cell_component_1k", 1, true, true, "digital_storage.item_storage_cell_1k", 1, true, true)]

    [TestCase("digital_storage.cell_component_1k", 2, true, true, "digital_storage.item_storage_cell_1k", 1, true, false)]
    [TestCase("digital_storage.cell_component_1k", 1, false, true, "digital_storage.item_storage_cell_1k", 1, true, false)]
    [TestCase("digital_storage.cell_component_1k", 1, true, false, "digital_storage.item_storage_cell_1k", 1, true, false)]
    [TestCase("digital_storage.cell_component_1k", 1, true, true, "digital_storage.item_storage_cell_4k", 1, true, false)]
    [TestCase("other", 1, true, true, "digital_storage.item_storage_cell_1k", 1, true, false)]
    [TestCase("digital_storage.cell_component_1k", 1, true, true, "digital_storage.item_storage_cell_1k", 2, true, false)]
    [TestCase("digital_storage.cell_component_1k", 1, true, true, "digital_storage.item_storage_cell_1k", 1, false, false)]
    public void RepairRules_RequireExactInstalledIdentity(string component, int count, bool locked, bool slot,
        string host, int hostCount, bool same, bool expected)
    {
        Assert.That(ItemContentRepairRules.IsInstalledComponent(component, count, locked, slot, host, hostCount, same),
            Is.EqualTo(expected));
    }

    [Test]
    public void RepairRules_RecognizeOnlyTheSoleExactNativeStuckFeature()
    {
        var native = NativeStuckFeature();
        var otherLocale = NativeStuckFeature(conditionDisplay: "localized stuck");
        var custom = NativeStuckFeature(category: "CUSTOM_CATEGORY");
        var notExposable = NativeStuckFeature(exposable: false);
        var exposed = NativeStuckFeature(exposed: true);
        var exposing = NativeStuckFeature(exposing: true);
        var notInitiallyShown = NativeStuckFeature(initiallyShown: false);
        var discovered = NativeStuckFeature(discovered: true);
        var publicHidden = NativeStuckFeature(publicHidden: true);
        var disabled = NativeStuckFeature(disabled: true);
        var alteredDisplay = NativeStuckFeature(conditionDisplay: "custom text");
        var alteredFeatureDisplay = NativeStuckFeature(actualDisplay: "custom feature text");
        var customTool = NativeStuckFeature(removedByTool: "custom tool");
        var customValue = NativeStuckFeature(valueModifier: 1);
        var preExposed = NativeStuckFeature(usePreExposeValue: true, preExposeValueModifier: -75);
        var emptyAction = NativeStuckFeature(actionId: string.Empty);
        var template = NativeStuckFeature();
        var displays = new[] { "stuck", "localized stuck" };

        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { native }, 42, template, displays, new[] { "native feature" }), Is.Zero);
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { otherLocale }, 42, template, displays, new[] { "native feature" }), Is.Zero);
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { custom }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { notExposable }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { exposed }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { exposing }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { notInitiallyShown }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { discovered }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { publicHidden }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { disabled }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { alteredDisplay }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { alteredFeatureDisplay }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { customTool }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { customValue }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { preExposed }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { emptyAction }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { native, custom }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { native, native }, 42, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.FindNativeStuckFeature(new[] { native }, 43, template, displays, new[] { "native feature" }), Is.EqualTo(-1));
    }

    [Test]
    public void RepairRules_MapMatchedFeatureToItsOriginalListPosition()
    {
        var indexes = new[] { 2, 5 };
        Assert.That(ItemContentRepairRules.ResolveNativeFeatureIndex(0, indexes), Is.EqualTo(2));
        Assert.That(ItemContentRepairRules.ResolveNativeFeatureIndex(1, indexes), Is.EqualTo(5));
        Assert.That(ItemContentRepairRules.ResolveNativeFeatureIndex(-1, indexes), Is.EqualTo(-1));
        Assert.That(ItemContentRepairRules.ResolveNativeFeatureIndex(2, indexes), Is.EqualTo(-1));
    }

    [TestCase("1k", 0, 80)]
    [TestCase("4k", 0, 80)]
    [TestCase("16k", 1, 180)]
    [TestCase("64k", 1, 180)]
    [TestCase("256k", 2, 280)]
    public void PriceRules_ApplyOnlyTheAllowedExpansionStages(string tierName, int stage, long expected)
    {
        var tier = StorageCellTier.All.Single(item => item.Name == tierName);
        Assert.That(ItemContentPriceRules.WithExpansion(80, stage, ModuleBayExpansion.MaxStage(tier), 100),
            Is.EqualTo(expected));
    }

    [TestCase("1k", 1)]
    [TestCase("1k", 2)]
    [TestCase("4k", 1)]
    [TestCase("16k", 2)]
    [TestCase("64k", 2)]
    [TestCase("256k", 3)]
    public void PriceRules_RejectAnExpansionStageBeyondTheTier(string tierName, int stage)
    {
        var tier = StorageCellTier.All.Single(item => item.Name == tierName);
        Assert.Throws<InvalidOperationException>((Action)(() =>
            ItemContentPriceRules.WithExpansion(80, stage, ModuleBayExpansion.MaxStage(tier), 100)));
    }

    private static ItemContentFeatureSnapshot NativeStuckFeature(string category = "CATEGORY_MODULE_STUCK",
        bool exposable = true, bool exposed = false, bool exposing = false, bool initiallyShown = true,
        bool discovered = false, bool publicHidden = false, bool disabled = false, string conditionDisplay = "stuck",
        string? actionId = null, string? publicDisplay = "native feature", string? actualDisplay = "native feature",
        string? removedByTool = null, int valueModifier = 0, bool usePreExposeValue = false,
        int preExposeValueModifier = 0) =>
        new("moduleStuck", category, "Normale", "Innate", 42, publicDisplay, actualDisplay, removedByTool,
            valueModifier, usePreExposeValue, preExposeValueModifier, true, true, exposable, exposed, exposing,
            initiallyShown, discovered, publicHidden, disabled, 0,
            NativeCondition(conditionDisplay, actionId), NativeCondition(conditionDisplay, actionId));

    private static ItemContentConditionSnapshot NativeCondition(string display = "stuck", string? actionId = null) =>
        new(display, "module_stuck", "CATEGORY_FRESHNESS", -75, 0, false, false, 0, actionId, 0);
}
