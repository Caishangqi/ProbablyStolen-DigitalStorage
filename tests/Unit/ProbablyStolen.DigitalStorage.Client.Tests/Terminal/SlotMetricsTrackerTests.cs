using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Client;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

public sealed class SlotMetricsTrackerTests
{
    private readonly List<string> _log = new();

    [SetUp]
    public void SetUp() => _log.Clear();

    [Test]
    public void StartsAtTheOriginalSlot()
    {
        Assert.That(new SlotMetricsTracker(_log.Add).Current, Is.EqualTo(SlotMetrics.Default));
    }

    [Test]
    public void Update_RaisesOnceWithBothValuesWhenTheMeasurementsChange()
    {
        var tracker = new SlotMetricsTracker(_log.Add);
        var seen = new List<SlotMetricsChangedEventArgs>();
        tracker.Changed += (sender, change) =>
        {
            Assert.That(sender, Is.SameAs(tracker));
            Assert.That(tracker.Current, Is.SameAs(change.Current), "Listeners read the new value.");
            seen.Add(change);
        };
        var large = SlotMetrics.Create(SizeTier.Large, SizeTier.Large, IconMarginTier.Standard, SizeTier.Default);

        Assert.That(tracker.Update(large), Is.True);

        Assert.That(seen, Has.Count.EqualTo(1));
        Assert.That(seen[0].Previous, Is.EqualTo(SlotMetrics.Default));
        Assert.That(seen[0].Current, Is.EqualTo(large));
        Assert.That(_log, Is.Empty);
    }

    [Test]
    public void Update_IgnoresAnEqualValue()
    {
        var tracker = new SlotMetricsTracker(_log.Add);
        var raised = 0;
        tracker.Changed += (_, _) => raised++;

        var same = SlotMetrics.Create(SizeTier.Default, SizeTier.Default, IconMarginTier.Standard, SizeTier.Default);
        Assert.That(tracker.Update(same), Is.False);

        Assert.That(raised, Is.Zero);
        Assert.That(tracker.Current, Is.SameAs(SlotMetrics.Default));
    }

    [Test]
    public void Update_TreatsAReducedSizeAsAChange()
    {
        var tracker = new SlotMetricsTracker(_log.Add);
        tracker.Update(SlotMetrics.Create(SizeTier.Large, SizeTier.Large, IconMarginTier.Standard, SizeTier.Default));
        var raised = 0;
        tracker.Changed += (_, _) => raised++;

        Assert.That(tracker.Update(SlotMetrics.Create(SizeTier.Large, SizeTier.Medium, IconMarginTier.Standard, SizeTier.Default)),
            Is.True, "A smaller screen changes the size in use while the choice stays.");
        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void Update_TreatsADockSizeChangeAsAChange()
    {
        var tracker = new SlotMetricsTracker(_log.Add);
        var raised = 0;
        tracker.Changed += (_, _) => raised++;

        Assert.That(tracker.Update(SlotMetrics.Create(SizeTier.Default, SizeTier.Default, IconMarginTier.Standard,
            SizeTier.Large)), Is.True, "Open windows rebuild their docks.");
        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void Update_KeepsNotifyingAfterAListenerThrows()
    {
        var tracker = new SlotMetricsTracker(_log.Add);
        var reached = false;
        tracker.Changed += (_, _) => throw new InvalidOperationException("broken listener");
        tracker.Changed += (_, _) => reached = true;
        var small = SlotMetrics.Create(SizeTier.Small, SizeTier.Small, IconMarginTier.Loose, SizeTier.Default);

        Assert.That(tracker.Update(small), Is.True);

        Assert.That(reached, Is.True);
        Assert.That(tracker.Current, Is.EqualTo(small));
        Assert.That(_log, Is.EqualTo(new[] { "slot_metrics listener=failed error=InvalidOperationException" }));
    }

    [Test]
    public void Update_StopsNotifyingARemovedListener()
    {
        var tracker = new SlotMetricsTracker(_log.Add);
        var raised = 0;
        EventHandler<SlotMetricsChangedEventArgs> listener = (_, _) => raised++;
        tracker.Changed += listener;
        tracker.Changed -= listener;

        tracker.Update(SlotMetrics.Create(SizeTier.Small, SizeTier.Small, IconMarginTier.Standard, SizeTier.Default));

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>((Action)(() => new SlotMetricsTracker(null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => new SlotMetricsTracker(_log.Add).Update(null!)));
    }
}
