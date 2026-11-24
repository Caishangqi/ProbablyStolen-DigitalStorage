using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class IoPortSettingsTests
{
    [Test]
    public void Default_IsPausedForwardAndEjectsWhenWorkIsComplete()
    {
        Assert.That(IoPortSettings.Default.Direction, Is.EqualTo(IoPortDirection.Forward));
        Assert.That(IoPortSettings.Default.CompletionMode, Is.EqualTo(IoPortCompletionMode.WorkComplete));
        Assert.That(IoPortSettings.Default.Running, Is.False);
    }

    [Test]
    public void ReverseDirection_ChangesOnlyDirection()
    {
        var settings = new IoPortSettings(IoPortDirection.Forward, IoPortCompletionMode.Full, running: false);

        var reversed = settings.ReverseDirection();

        Assert.That((reversed.Direction, reversed.CompletionMode, reversed.Running),
            Is.EqualTo((IoPortDirection.Reverse, IoPortCompletionMode.Full, false)));
        Assert.That(settings.Direction, Is.EqualTo(IoPortDirection.Forward));
        Assert.That(reversed.ReverseDirection().SameAs(settings), Is.True);
    }

    [Test]
    public void WithCompletionModeAndRunning_ChangeOnlyTheirOwnSetting()
    {
        var settings = IoPortSettings.Default.WithRunning(true);

        var empty = settings.WithCompletionMode(IoPortCompletionMode.Empty);
        var paused = settings.WithRunning(false);

        Assert.That((empty.Direction, empty.CompletionMode, empty.Running),
            Is.EqualTo((IoPortDirection.Forward, IoPortCompletionMode.Empty, true)));
        Assert.That((paused.Direction, paused.CompletionMode, paused.Running),
            Is.EqualTo((IoPortDirection.Forward, IoPortCompletionMode.WorkComplete, false)));
        Assert.That(paused.SameAs(settings), Is.False);
    }

    [Test]
    public void Constructor_RejectsUndefinedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => _ = new IoPortSettings((IoPortDirection)9, IoPortCompletionMode.Empty, true)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => _ = new IoPortSettings(IoPortDirection.Forward, (IoPortCompletionMode)9, true)));
    }
}
