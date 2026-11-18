using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

public sealed class WindowBackCoordinatorTests
{
    [Test]
    public void Dispatch_UsesFocusAndNativeTiePriority()
    {
        var coordinator = new WindowBackCoordinator();
        var calls = string.Empty;
        using var first = coordinator.Register(() => true, () => { calls += "a"; return true; });
        using var second = coordinator.Register(() => true, () => { calls += "b"; return true; });
        first.Focus(2);
        second.Focus(4);

        Assert.That(coordinator.Dispatch(1, 4), Is.False);
        Assert.That(coordinator.Dispatch(2, 3), Is.True);
        Assert.That(calls, Is.EqualTo("b"));
        first.Focus(6);
        Assert.That(coordinator.Dispatch(3, null), Is.True);
        Assert.That(calls, Is.EqualTo("ba"));
    }

    [Test]
    public void Dispatch_SkippedWindowContinuesToNextEligibleCandidate()
    {
        var coordinator = new WindowBackCoordinator();
        var calls = string.Empty;
        using var next = coordinator.Register(() => true, () => { calls += "n"; return true; });
        using var top = coordinator.Register(() => true, () => { calls += "t"; return false; });
        next.Focus(3);
        top.Focus(5);

        Assert.That(coordinator.Dispatch(1, 2), Is.True);
        Assert.That(calls, Is.EqualTo("tn"));
    }

    [Test]
    public void Dispatch_SkipDoesNotConsumeAndNativeFallbackRemainsAvailable()
    {
        var coordinator = new WindowBackCoordinator();
        var calls = 0;
        using var window = coordinator.Register(() => true, () => { calls++; return false; });
        window.Focus(5);

        Assert.That(coordinator.Dispatch(1, null), Is.False);
        Assert.That(coordinator.Dispatch(1, null), Is.False);
        Assert.That(coordinator.Dispatch(2, 5), Is.False);
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void Dispatch_TiesKeepExistingRegistrationOrder()
    {
        var coordinator = new WindowBackCoordinator();
        var calls = string.Empty;
        using var first = coordinator.Register(() => true, () => { calls += "a"; return true; });
        using var second = coordinator.Register(() => true, () => { calls += "b"; return true; });
        first.Focus(4);
        second.Focus(4);

        Assert.That(coordinator.Dispatch(1, null), Is.True);
        Assert.That(calls, Is.EqualTo("a"));
    }

    [Test]
    public void Dispatch_RechecksActivityAndOwnershipBeforeCallback()
    {
        var coordinator = new WindowBackCoordinator();
        var active = true;
        var calls = 0;
        WindowBackCoordinator.Registration? disposed = null;
        using var lower = coordinator.Register(() => true, () => { calls++; return true; });
        disposed = coordinator.Register(() => active, () => throw new InvalidOperationException());
        lower.Focus(1);
        disposed.Focus(2);
        active = false;

        Assert.That(coordinator.Dispatch(1, null), Is.True);
        Assert.That(calls, Is.EqualTo(1));

        active = true;
        disposed.Dispose();
        Assert.That(coordinator.Dispatch(2, null), Is.True);
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void Dispatch_ReentryIsHandledButSkippedOuterDispatchDoesNotConsume()
    {
        var coordinator = new WindowBackCoordinator();
        var count = 0;
        WindowBackCoordinator.Registration? top = null;
        using var lower = coordinator.Register(() => true, () => { count += 10; return true; });
        top = coordinator.Register(() => true, () =>
        {
            count++;
            Assert.That(coordinator.Dispatch(1, null), Is.True);
            return false;
        });
        lower.Focus(1);
        top.Focus(2);

        Assert.That(coordinator.Dispatch(1, null), Is.True);
        Assert.That(count, Is.EqualTo(11));
        Assert.That(coordinator.Dispatch(1, null), Is.True);
        Assert.That(count, Is.EqualTo(11));
    }

    [Test]
    public void Dispatch_ExceptionPropagatesAndReleasesInProgressState()
    {
        var coordinator = new WindowBackCoordinator();
        var shouldThrow = true;
        var calls = 0;
        using var window = coordinator.Register(() => true, () =>
        {
            calls++;
            if (shouldThrow) throw new InvalidOperationException("callback failure");
            return true;
        });
        window.Focus(1);

        Assert.Throws<InvalidOperationException>(new Action(() => coordinator.Dispatch(1, null)));
        shouldThrow = false;
        Assert.That(coordinator.Dispatch(1, null), Is.True);
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void Dispatch_SnapshotExcludesWindowsAddedDuringCallback()
    {
        var coordinator = new WindowBackCoordinator();
        WindowBackCoordinator.Registration? added = null;
        var addedCalls = 0;
        using var top = coordinator.Register(() => true, () =>
        {
            added = coordinator.Register(() => true, () => { addedCalls++; return true; });
            added.Focus(10);
            return false;
        });
        top.Focus(1);

        Assert.That(coordinator.Dispatch(1, null), Is.False);
        Assert.That(addedCalls, Is.Zero);
        Assert.That(coordinator.Dispatch(2, null), Is.True);
        Assert.That(addedCalls, Is.EqualTo(1));
        added?.Dispose();
    }

    [TestCase(1, 2, false)]
    [TestCase(2, 2, false)]
    [TestCase(3, 2, true)]
    public void AbovePaper_UsesNativePaperTieBreak(long window, long paper, bool expected) =>
        Assert.That(WindowBackCoordinator.AbovePaper(window, paper), Is.EqualTo(expected));
}
