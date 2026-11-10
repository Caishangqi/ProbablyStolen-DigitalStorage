using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class DictionaryLoadGateTests
{
    [Test]
    public void LoadsOnlyWhenEnabledAndAssetsAreReadyRegardlessOfOrder()
    {
        var attempts = 0;
        using var gate = new DictionaryLoadGate(() => attempts++);

        Assert.That(gate.Update(true), Is.False);
        Assert.That(gate.MarkAssetsReady(), Is.True);
        Assert.That(attempts, Is.EqualTo(1));
        Assert.That(gate.Update(false), Is.False);
        Assert.That(gate.Update(true), Is.False);
        Assert.That(attempts, Is.EqualTo(1));

        var secondAttempts = 0;
        using var second = new DictionaryLoadGate(() => secondAttempts++);
        Assert.That(second.MarkAssetsReady(), Is.False);
        Assert.That(second.Update(false), Is.False);
        Assert.That(secondAttempts, Is.Zero);
        Assert.That(second.Update(true), Is.True);
        Assert.That(secondAttempts, Is.EqualTo(1));
    }

    [Test]
    public void FailedAttemptIsNotRetriedAfterOffOnOrReentry()
    {
        var attempts = 0;
        DictionaryLoadGate? gate = null;
        gate = new DictionaryLoadGate(() =>
        {
            attempts++;
            gate!.MarkAssetsReady();
            throw new InvalidOperationException("reader failed");
        });
        gate.MarkAssetsReady();
        Action attempt = () => gate.Update(true);
        Assert.Throws<InvalidOperationException>(attempt);
        Assert.That(gate.Attempted, Is.True);
        Assert.That(gate.Update(false), Is.False);
        Assert.That(gate.Update(true), Is.False);
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public void DisposedGateDoesNotLoadOrRetainCallback()
    {
        var attempts = 0;
        var gate = new DictionaryLoadGate(() => attempts++);
        gate.MarkAssetsReady();
        gate.Dispose();

        Assert.That(gate.Update(true), Is.False);
        Assert.That(gate.MarkAssetsReady(), Is.False);
        Assert.That(attempts, Is.Zero);
    }
}
