using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Events;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class StorageEventBusTests
{
    [Test]
    public void Post_ReachesOnlySubscribersOfTheEventType()
    {
        using var bus = new StorageEventBus(_ => { });
        var reasons = new List<NetworkChangeReason>();
        var other = 0;
        bus.Subscribe<NetworkChanged>(e => reasons.Add(e.Reason));
        bus.Subscribe<string>(_ => other++);

        bus.Post(new NetworkChanged(NetworkChangeReason.CellRemoved));

        Assert.That(reasons, Is.EqualTo(new[] { NetworkChangeReason.CellRemoved }));
        Assert.That(other, Is.Zero);
    }

    [Test]
    public void DisposingSubscription_StopsDeliveryAndIsIdempotent()
    {
        using var bus = new StorageEventBus(_ => { });
        var count = 0;
        var subscription = bus.Subscribe<NetworkChanged>(_ => count++);

        subscription.Dispose();
        subscription.Dispose();
        bus.Post(new NetworkChanged(NetworkChangeReason.MachineAdded));

        Assert.That(count, Is.Zero);
    }

    [Test]
    public void FailingSubscriber_IsReportedWithoutBlockingOthers()
    {
        var errors = new List<string>();
        using var bus = new StorageEventBus(errors.Add);
        var reached = false;
        bus.Subscribe<NetworkChanged>(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe<NetworkChanged>(_ => reached = true);

        bus.Post(new NetworkChanged(NetworkChangeReason.PowerCycle));

        Assert.That(reached, Is.True);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain(nameof(InvalidOperationException)));
    }

    [Test]
    public void UnsubscribingDuringDispatch_AppliesFromTheNextPost()
    {
        using var bus = new StorageEventBus(_ => { });
        var second = 0;
        IDisposable? secondSubscription = null;
        bus.Subscribe<NetworkChanged>(_ => secondSubscription!.Dispose());
        secondSubscription = bus.Subscribe<NetworkChanged>(_ => second++);

        bus.Post(new NetworkChanged(NetworkChangeReason.MachineRemoved));
        bus.Post(new NetworkChanged(NetworkChangeReason.MachineRemoved));

        Assert.That(second, Is.EqualTo(1));
    }

    [Test]
    public void DisposedBus_IgnoresPostsAndLateSubscriptions()
    {
        var bus = new StorageEventBus(_ => { });
        var count = 0;
        var subscription = bus.Subscribe<NetworkChanged>(_ => count++);

        bus.Dispose();
        bus.Post(new NetworkChanged(NetworkChangeReason.BatteryChanged));
        subscription.Dispose();

        var late = bus.Subscribe<NetworkChanged>(_ => count++);
        bus.Post(new NetworkChanged(NetworkChangeReason.BatteryChanged));
        late.Dispose();

        Assert.That(count, Is.Zero);
    }

    [Test]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>((System.Action)(() => _ = new StorageEventBus(null!)));
        using var bus = new StorageEventBus(_ => { });
        Assert.Throws<ArgumentNullException>((System.Action)(() => bus.Subscribe<NetworkChanged>(null!)));
    }
}
