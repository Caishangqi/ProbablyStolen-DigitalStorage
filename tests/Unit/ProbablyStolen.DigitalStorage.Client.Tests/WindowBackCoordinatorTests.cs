using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

public sealed class WindowBackCoordinatorTests
{
    [Test]
    public void Dispatch_PaperAfterTerminalThenNative_TracksLayerAndBackOrder()
    {
        var coordinator = new WindowBackCoordinator();
        var terminalOpen = true;
        using var terminal = coordinator.Register(() => terminalOpen, () => terminalOpen = false);
        terminal.Focus(1);
        long? paper = 2;
        long? nativeWindow = 3;

        Assert.That(WindowBackCoordinator.AbovePaper(1, paper), Is.False);
        Assert.That(coordinator.Dispatch(1, nativeWindow), Is.False);
        nativeWindow = null;
        Assert.That(coordinator.Dispatch(2, paper), Is.False);
        paper = null;
        Assert.That(WindowBackCoordinator.AbovePaper(1, paper), Is.True);
        Assert.That(coordinator.Dispatch(3, nativeWindow), Is.True);
        Assert.That(terminalOpen, Is.False);
    }

    [TestCase(1, 2, false)]
    [TestCase(2, 2, false)]
    [TestCase(3, 2, true)]
    public void AbovePaper_UsesSameTieBreakAsDispatch(long window, long paper, bool expected) =>
        Assert.That(WindowBackCoordinator.AbovePaper(window, paper), Is.EqualTo(expected));

    [Test]
    public void Dispatch_UsesFocusAndNativeFallback()
    {
        var coordinator = new WindowBackCoordinator();
        var calls = string.Empty;
        using var first = coordinator.Register(() => true, () => calls += "a");
        using var second = coordinator.Register(() => true, () => calls += "b");
        first.Focus(2);
        second.Focus(4);
        Assert.That(coordinator.Dispatch(1, 5), Is.False);
        Assert.That(coordinator.Dispatch(2, 3), Is.True);
        Assert.That(calls, Is.EqualTo("b"));
        first.Focus(6);
        Assert.That(coordinator.Dispatch(3, null), Is.True);
        Assert.That(calls, Is.EqualTo("ba"));
    }

    [Test]
    public void Dispatch_ReentryAndRemoval_HandleOnlyOncePerFrame()
    {
        var coordinator = new WindowBackCoordinator();
        var count = 0;
        WindowBackCoordinator.Registration? top = null;
        using var other = coordinator.Register(() => true, () => count += 10);
        top = coordinator.Register(() => true, () =>
        {
            count++;
            top!.Dispose();
            Assert.That(coordinator.Dispatch(10, null), Is.True);
        });
        top.Focus(2);
        Assert.That(coordinator.Dispatch(10, null), Is.True);
        Assert.That(coordinator.Dispatch(10, null), Is.True);
        Assert.That(count, Is.EqualTo(1));
        Assert.That(coordinator.Dispatch(11, null), Is.True);
        Assert.That(count, Is.EqualTo(11));
    }

    [Test]
    public void Dispatch_InactiveAndDisposedTargets_FallBack()
    {
        var coordinator = new WindowBackCoordinator();
        var active = false;
        var window = coordinator.Register(() => active, () => throw new InvalidOperationException());
        window.Focus(1);
        Assert.That(coordinator.Dispatch(1, null), Is.False);
        active = true;
        window.Dispose();
        Assert.That(coordinator.Dispatch(2, null), Is.False);
    }
}
