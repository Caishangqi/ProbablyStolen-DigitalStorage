using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class PartitionWindowControllerTests
{
    private const string Leaf = "44534944000000010a01010000000161000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";
    private static ItemKey Key() => ItemKey.FromCanonical("a", Convert.FromHexString(Leaf));

    [Test]
    public void Open_UnknownReadDoesNotCreateDraftAndReleasesSource()
    {
        var source = new Source { Readable = false };
        var controller = new PartitionWindowController();
        Assert.That(controller.Open(source), Is.False);
        Assert.That(controller.IsEditing, Is.False);
        Assert.That(source.Disposed, Is.True);
    }

    [Test]
    public void GhostIsCanonicalDeduplicatedBoundedAndNeverConsumesSource()
    {
        var source = new Source(); var controller = new PartitionWindowController();
        Assert.That(controller.Open(source), Is.True);
        Assert.That(controller.AddGhost(Key()), Is.True);
        Assert.That(controller.AddGhost(Key()), Is.True);
        Assert.That(controller.Keys.Count, Is.EqualTo(1));
        Assert.That(source.Submits, Is.Zero);
        Assert.That(controller.AddGhost(new ItemKey("legacy")), Is.False);
    }

    [Test]
    public void ContentsReplacementIsAllOrNothingAndRejectsOverLimit()
    {
        var source = new Source(); var controller = new PartitionWindowController();
        controller.Open(source); controller.AddGhost(Key());
        source.Contents = new ItemKey[] { Key(), new ItemKey("legacy") };
        Assert.That(controller.SetFromContents(), Is.False);
        Assert.That(controller.Keys.Count, Is.EqualTo(1));
        source.Contents = Many(64);
        Assert.That(controller.SetFromContents(), Is.False);
        Assert.That(controller.Keys.Count, Is.EqualTo(1));
    }

    [Test]
    public void GhostDraftAcceptsSixtyThreeAndRejectsTheSixtyFourth()
    {
        var source = new Source { Contents = Many(63) };
        var controller = new PartitionWindowController();
        controller.Open(source);
        Assert.That(controller.SetFromContents(), Is.True);
        Assert.That(controller.Keys.Count, Is.EqualTo(63));
        Assert.That(controller.AddGhost(Many(64)[63]), Is.False);
        Assert.That(controller.Keys.Count, Is.EqualTo(63));
    }

    [Test]
    public void StaleSourceClosesAndDisposesDraft()
    {
        var source = new Source { Partition = CellPartition.FromUserSubmission(new[] { Key() }, 63) };
        var controller = new PartitionWindowController();
        controller.Open(source); controller.AddGhost(Key()); source.Current = false;
        Assert.That(controller.Clear(), Is.False);
        Assert.That(controller.IsEditing, Is.False);
        Assert.That(source.Disposed, Is.True);
        Assert.That(controller.Keys, Is.Empty);
    }

    [Test]
    public void RejectedOrThrowingApplyRetainsDraftAndCanRetry()
    {
        var source = new Source(); var controller = new PartitionWindowController();
        controller.Open(source); controller.AddGhost(Key());
        source.Accept = false;
        Assert.That(controller.Apply(), Is.False);
        Assert.That(controller.Keys.Count, Is.EqualTo(1));
        source.Throw = true;
        Assert.That(controller.Apply(), Is.False);
        Assert.That(controller.Keys.Count, Is.EqualTo(1));
        source.Throw = false; source.Accept = true;
        Assert.That(controller.Apply(), Is.True);
        Assert.That(source.Submits, Is.EqualTo(3));
        Assert.That(source.Disposed, Is.True);
    }

    [Test]
    public void ReopenDisposesPriorSessionAndSecondApplyCannotResubmit()
    {
        var first = new Source(); var second = new Source();
        var controller = new PartitionWindowController();
        controller.Open(first); controller.AddGhost(Key());
        Assert.That(controller.Open(second), Is.True);
        Assert.That(first.Disposed, Is.True);
        Assert.That(controller.Apply(), Is.True);
        Assert.That(controller.Apply(), Is.False);
        Assert.That(second.Submits, Is.EqualTo(1));
    }

    [Test]
    public void GhostGestureCapturesWhileLiveThenCancelsBeforeAddingOnlyTheKey()
    {
        var sequence = new List<string>();
        var result = PartitionGhostGesture.Execute(true, true,
            () => { sequence.Add("source"); return true; },
            () => { sequence.Add("capture"); return Key(); },
            () => { sequence.Add("cancel"); return true; },
            _ => sequence.Add("accept"));
        Assert.That(result.Consumed, Is.True);
        Assert.That(result.CancellationConfirmed, Is.True);
        Assert.That(result.Accepted, Is.True);
        Assert.That(sequence, Is.EqualTo(new[] { "source", "capture", "cancel", "source", "accept" }));
    }

    [Test]
    public void GhostGestureWrongTargetStaleCaptureExceptionAndUncertainCancelNeverAccept()
    {
        var captureCalls = 0;
        var cancelCalls = 0;
        var acceptedCalls = 0;
        var wrong = PartitionGhostGesture.Execute(false, true, () => true,
            () => { captureCalls++; return Key(); }, () => { cancelCalls++; return true; }, _ => acceptedCalls++);
        Assert.That(wrong.Consumed, Is.False);
        Assert.That(captureCalls + cancelCalls + acceptedCalls, Is.Zero);

        var stale = PartitionGhostGesture.Execute(true, true, () => false,
            () => { captureCalls++; return Key(); }, () => { cancelCalls++; return true; }, _ => acceptedCalls++);
        Assert.That(stale.Consumed, Is.True);
        Assert.That(stale.CancellationConfirmed, Is.True);
        Assert.That(stale.Accepted, Is.False);
        Assert.That(captureCalls, Is.Zero);
        Assert.That(cancelCalls, Is.EqualTo(1));

        var captureFailure = PartitionGhostGesture.Execute(true, true, () => true,
            () => throw new InvalidOperationException(), () => { cancelCalls++; return true; }, _ => acceptedCalls++);
        Assert.That(captureFailure.Consumed, Is.True);
        Assert.That(captureFailure.Accepted, Is.False);
        Assert.That(cancelCalls, Is.EqualTo(2));

        var uncertain = PartitionGhostGesture.Execute(true, true, () => true,
            () => { captureCalls++; return Key(); },
            () => { cancelCalls++; return false; }, _ => acceptedCalls++);
        Assert.That(uncertain.Consumed, Is.True);
        Assert.That(uncertain.CancellationConfirmed, Is.False);
        Assert.That(uncertain.Accepted, Is.False);
        Assert.That(captureCalls, Is.EqualTo(1));
        Assert.That(cancelCalls, Is.EqualTo(3));
        Assert.That(acceptedCalls, Is.Zero);

        var checks = 0;
        var changedDuringCancel = PartitionGhostGesture.Execute(true, true,
            () => ++checks == 1,
            Key,
            () => true,
            _ => acceptedCalls++);
        Assert.That(changedDuringCancel.CancellationConfirmed, Is.True);
        Assert.That(changedDuringCancel.Accepted, Is.False);
        Assert.That(checks, Is.EqualTo(2));
        Assert.That(acceptedCalls, Is.Zero);
    }

    [Test]
    public void ReentrantOpenDuringReadRejectsReplacementAndClosesOriginal()
    {
        var controller = new PartitionWindowController();
        var replacement = new Source();
        var original = new Source { OnRead = () => Assert.That(controller.Open(replacement), Is.False) };
        Assert.That(controller.Open(original), Is.False);
        Assert.That(original.Disposed, Is.True);
        Assert.That(replacement.Disposed, Is.True);
        Assert.That(controller.Keys, Is.Empty);
    }

    [Test]
    public void ReentrantCloseOrStaleContentsCannotInstallAnotherDraft()
    {
        var controller = new PartitionWindowController();
        var source = new Source { Partition = CellPartition.FromUserSubmission(new[] { Key() }, 63) };
        Assert.That(controller.Open(source), Is.True);
        source.OnContents = () => controller.Close();
        Assert.That(controller.SetFromContents(), Is.False);
        Assert.That(controller.Keys, Is.Empty);
        Assert.That(source.Disposed, Is.True);

        var stale = new Source { Partition = CellPartition.FromUserSubmission(new[] { Key() }, 63) };
        controller.Open(stale);
        stale.OnContents = () => stale.Current = false;
        Assert.That(controller.SetFromContents(), Is.False);
        Assert.That(controller.Keys, Is.Empty);
        Assert.That(stale.Disposed, Is.True);
    }

    [Test]
    public void ApplyCloseAndDisposeFailurePreserveAcceptedOutcomeAndReleaseOnce()
    {
        var controller = new PartitionWindowController();
        var source = new Source { ThrowOnDispose = true };
        controller.Open(source); controller.AddGhost(Key());
        source.OnSubmit = () => controller.Close();
        Assert.That(controller.Apply(), Is.True);
        Assert.That(controller.Status, Is.EqualTo("applied_cleanup_failed"));
        Assert.That(controller.Keys, Is.Empty);
        Assert.That(source.DisposeCalls, Is.EqualTo(1));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void ApplyCloseReentryRejectOrThrowReleasesOnceWithoutOrphan(bool accept, bool throwSubmit)
    {
        var controller = new PartitionWindowController();
        var source = new Source { Accept = accept, Throw = throwSubmit, ThrowOnDispose = true };
        controller.Open(source); controller.AddGhost(Key());
        source.OnSubmit = () => controller.Close();
        Assert.That(controller.Apply(), Is.EqualTo(accept && !throwSubmit));
        Assert.That(controller.IsEditing, Is.False);
        Assert.That(controller.Keys, Is.Empty);
        Assert.That(source.DisposeCalls, Is.EqualTo(1));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void ApplyReopenReentryCannotReplaceSourceDuringSubmission(bool accept, bool throwSubmit)
    {
        var controller = new PartitionWindowController();
        var replacement = new Source();
        var original = new Source { Accept = accept, Throw = throwSubmit };
        controller.Open(original); controller.AddGhost(Key());
        original.OnSubmit = () => Assert.That(controller.Open(replacement), Is.False);
        Assert.That(controller.Apply(), Is.EqualTo(accept && !throwSubmit));
        Assert.That(original.DisposeCalls, Is.EqualTo(1));
        Assert.That(replacement.DisposeCalls, Is.EqualTo(1));
        Assert.That(controller.Keys, Is.Empty);
    }

    [Test]
    public void ReentrantContentsReplacementCannotUseOriginalKeysForReplacement()
    {
        var controller = new PartitionWindowController();
        var replacement = new Source();
        var original = new Source { Partition = CellPartition.FromUserSubmission(new[] { Key() }, 63) };
        controller.Open(original);
        original.Contents = Many(2);
        original.OnContents = () => Assert.That(controller.Open(replacement), Is.False);
        Assert.That(controller.SetFromContents(), Is.False);
        Assert.That(controller.Keys, Is.Empty);
        Assert.That(original.Disposed, Is.True);
        Assert.That(replacement.Disposed, Is.True);
    }

    [Test]
    public void ReentrantOpenFromCurrentValidationCannotBindThePriorPartition()
    {
        var controller = new PartitionWindowController();
        var replacement = new Source();
        var original = new Source { OnCurrent = () => Assert.That(controller.Open(replacement), Is.False) };
        Assert.That(controller.Open(original), Is.False);
        Assert.That(original.DisposeCalls, Is.EqualTo(1));
        Assert.That(replacement.DisposeCalls, Is.EqualTo(1));
        Assert.That(controller.Keys, Is.Empty);
    }

    [Test]
    public void FailedOpenAndStaleCloseHidePreviouslyValidatedBaseline()
    {
        var controller = new PartitionWindowController();
        var valid = new Source { Partition = CellPartition.FromUserSubmission(new[] { Key() }, 63) };
        Assert.That(controller.Open(valid), Is.True);
        Assert.That(controller.Keys, Has.Count.EqualTo(1));
        var failed = new Source { Readable = false, ThrowOnDispose = true };
        Assert.That(controller.Open(failed), Is.False);
        Assert.That(controller.Keys, Is.Empty);
        Assert.That(failed.Disposed, Is.True);
        Assert.That(failed.DisposeCalls, Is.EqualTo(1));

        var oldSource = new Source { ThrowOnDispose = true };
        var replacement = new Source();
        controller.Open(oldSource);
        Assert.That(controller.Open(replacement), Is.True);
        Assert.That(controller.Status, Is.EqualTo("cleanup_failed"));
        Assert.That(oldSource.DisposeCalls, Is.EqualTo(1));
        controller.Close();
        Assert.That(replacement.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void ReentrantCloseFromCurrentAndDisposeCallbacksDoesNotRebindOrDoubleRelease()
    {
        var controller = new PartitionWindowController();
        var opening = new Source { OnCurrent = () => controller.Close() };
        Assert.That(controller.Open(opening), Is.False);
        Assert.That(opening.DisposeCalls, Is.EqualTo(1));
        Assert.That(controller.Keys, Is.Empty);

        var closing = new Source { ThrowOnDispose = true };
        controller.Open(closing);
        closing.OnDispose = () => controller.Close();
        controller.Close();
        Assert.That(closing.DisposeCalls, Is.EqualTo(1));
        Assert.That(controller.Status, Is.EqualTo("cleanup_failed"));
        Assert.That(controller.Keys, Is.Empty);
    }

    [Test]
    public void IdentityLabelsDistinguishCanonicalStatesWithSameIdentifier()
    {
        var first = Many(2)[0];
        var second = Many(2)[1];
        Assert.That(first.Identifier, Is.EqualTo(second.Identifier));
        Assert.That(PartitionIdentityLabel.Format(first), Is.Not.EqualTo(PartitionIdentityLabel.Format(second)));
    }

    [Test]
    public void CloseDisposesSourceAndDropsKeys()
    {
        var source = new Source(); var controller = new PartitionWindowController();
        controller.Open(source); controller.AddGhost(Key()); controller.Dispose();
        Assert.That(source.Disposed, Is.True);
        Assert.That(controller.IsEditing, Is.False);
        Assert.That(controller.Keys, Is.Empty);
    }

    private static IReadOnlyList<ItemKey> Many(int count)
    {
        var result = new List<ItemKey>();
        for (var index = 0; index < count; index++)
            result.Add(CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(
                "a", Array.Empty<string>(), Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>(),
                name: "item-" + index)));
        return result;
    }

    private sealed class Source : IPartitionWindowSource
    {
        public bool Current = true, Readable = true, Accept = true, Throw, ThrowOnDispose;
        public bool Disposed;
        public int Submits, DisposeCalls;
        public Action? OnRead, OnCurrent, OnContents, OnSubmit, OnDispose;
        public CellPartition Partition = CellPartition.Unfiltered;
        public IReadOnlyList<ItemKey>? Contents = Array.Empty<ItemKey>();
        public bool IsCurrent { get { OnCurrent?.Invoke(); return Current; } }
        public bool TryRead(out CellPartition? partition) { OnRead?.Invoke(); partition = Readable ? Partition : null; return Readable; }
        public bool TryReadContents(out IReadOnlyList<ItemKey>? keys) { OnContents?.Invoke(); keys = Contents; return true; }
        public bool TrySubmit(CellPartition candidate) { Submits++; OnSubmit?.Invoke(); if (Throw) throw new InvalidOperationException(); return Accept; }
        public void Dispose() { DisposeCalls++; Disposed = true; OnDispose?.Invoke(); if (ThrowOnDispose) throw new InvalidOperationException(); }
    }
}
