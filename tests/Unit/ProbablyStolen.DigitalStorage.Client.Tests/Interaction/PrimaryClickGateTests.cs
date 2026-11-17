using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Interaction;

[TestFixture]
internal sealed class PrimaryClickGateTests
{
    [Test]
    public void ValidClick_UnityBeforeNativeRelease_RemainsEligibleWithoutDragSubmission()
    {
        var gate = new PrimaryClickGate();
        gate.BeginPress();
        Assert.That(gate.SuppressesClick(true, true, false), Is.False);
        // The click's refresh may cancel the controller before the native release observes it.
        Assert.That(gate.CompleteRelease(false, false), Is.True);
        Assert.That(gate.SuppressesClick(false, false, true), Is.True);
    }

    [Test]
    public void ValidClick_NativeReleaseBeforeUnity_RemainsEligibleAfterWaitingEnds()
    {
        var gate = new PrimaryClickGate();
        gate.BeginPress();
        Assert.That(gate.CompleteRelease(true, false), Is.False);
        Assert.That(gate.SuppressesClick(false, false, false), Is.False);
    }

    [Test]
    public void PageInvalidation_ThenNativeRelease_BlocksLateUnityClickInLaterFrame()
    {
        var gate = new PrimaryClickGate();
        gate.BeginPress();
        Assert.That(gate.SuppressesClick(true, true, false), Is.False);
        Assert.That(gate.SuppressesClick(true, false, false), Is.True);
        Assert.That(gate.CompleteRelease(false, false), Is.True);
        Assert.That(gate.SuppressesClick(false, false, false), Is.True,
            "Suppression must outlive the native release frame and the cleared owner flag.");
    }

    [Test]
    public void CancelledPress_StaysBlockedUntilAnotherAcceptedPress()
    {
        var gate = new PrimaryClickGate();
        gate.BeginPress();
        gate.Invalidate();
        Assert.That(gate.SuppressesClick(false, false, false), Is.True);
        Assert.That(gate.CompleteRelease(true, false), Is.True,
            "Release cannot revive an explicitly invalidated press.");
        gate.BeginPress();
        Assert.That(gate.SuppressesClick(true, true, false), Is.False);
        Assert.That(gate.CompleteRelease(true, false), Is.False);
        Assert.That(gate.SuppressesClick(false, false, false), Is.False);
    }

    [Test]
    public void ConfirmedDrag_BlocksClickBeforeAndAfterConsumedRelease()
    {
        var gate = new PrimaryClickGate();
        gate.BeginPress();
        Assert.That(gate.SuppressesClick(true, false, false), Is.True);
        Assert.That(gate.CompleteRelease(false, true), Is.True);
        Assert.That(gate.SuppressesClick(false, false, true), Is.True);
        Assert.That(gate.SuppressesClick(false, false, false), Is.True);
    }

    [Test]
    public void ConsumedRelease_CannotBeOverriddenByAnAllowance()
    {
        var gate = new PrimaryClickGate();
        gate.BeginPress();
        Assert.That(gate.CompleteRelease(true, true), Is.True);
        Assert.That(gate.SuppressesClick(false, true, false), Is.True);
        gate.BeginPress();
        Assert.That(gate.SuppressesClick(true, true, true), Is.True,
            "The input bridge's consumed-frame guard remains authoritative.");
    }
}
