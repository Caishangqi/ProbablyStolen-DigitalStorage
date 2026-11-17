using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
internal sealed class TerminalDragControllerTests
{
    [Test]
    public void SelectionDrag_UsesFinalSlotOrderAndGroupMemberOrder()
    {
        var fixture = CreateFixture();
        var observedSelection = Array.Empty<string>();
        TerminalDragBatch? observedBatch = null;
        var refreshes = 0;
        var target = OpaqueTarget();
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) =>
            {
                observedSelection = selected.ToArray();
                observedBatch = fixture.Menu.CreateDragBatch(shown, current, selected, revision, requested);
                return observedBatch;
            },
            (_, _) =>
            {
                refreshes++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, observedBatch!.RequestedPieces,
                    observedBatch.RequestedPieces, "done");
            });

        Assert.That(controller.Press(Point(0, 0), TerminalDragRegion.Content, null, fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(24, 12));
        Assert.That(controller.Release(Point(24, 12)), Is.True);
        Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Selected));

        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(20, 20));
        Assert.That(controller.Release(Point(20, 20)), Is.True);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(observedSelection, Is.EqualTo(new[] { "group-fruit", "group-stone" }));
            Assert.That(observedBatch, Is.Not.Null);
            Assert.That(observedBatch!.Steps.Select(step => step.Key), Is.EqualTo(fixture.ExpectedStepKeys));
            Assert.That(observedBatch.Steps.Select(step => step.Pieces), Is.EqualTo(new long[] { 6, 5, 4 }));
            Assert.That(refreshes, Is.EqualTo(1));
            Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Idle));
        }));
    }

    [Test]
    public void SingleDrag_UsesAdjustedPiecesAndRefreshesOnceAfterPartialResult()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var extractionCalls = 0;
        var refreshes = 0;
        var available = 11L;
        TerminalExtractionResult? result = null;
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (batch, _) =>
            {
                extractionCalls++;
                available -= 3;
                result = TerminalExtractionResult.Create(TerminalExtractionStatus.Partial,
                    batch.RequestedPieces, 3, "partial");
                return result;
            },
            (_, _) => refreshes++);

        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(9, 9));
        controller.Wheel(-5, Point(9, 9));
        Assert.That(controller.Release(Point(9, 9)), Is.True);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(extractionCalls, Is.EqualTo(1));
            Assert.That(result!.RequestedAmount, Is.EqualTo(6));
            Assert.That(result.ExtractedAmount, Is.EqualTo(3));
            Assert.That(available, Is.EqualTo(8));
            Assert.That(refreshes, Is.EqualTo(1));
        }));
    }

    [Test]
    public void FirstItemRejected_IsReportedWithoutAnotherControllerSubmission()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var extractionCalls = 0;
        var refreshes = 0;
        TerminalExtractionResult? observed = null;
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (batch, _) =>
            {
                extractionCalls++;
                observed = TerminalExtractionResult.Create(TerminalExtractionStatus.TargetRejected,
                    batch.RequestedPieces, 0, "rejected");
                return observed;
            },
            (_, _) => refreshes++);

        SelectBoth(controller, fixture);
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(20, 20));
        controller.Release(Point(20, 20));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(extractionCalls, Is.EqualTo(1));
            Assert.That(observed!.ExtractedAmount, Is.Zero);
            Assert.That(observed.Status, Is.EqualTo(TerminalExtractionStatus.TargetRejected));
            Assert.That(refreshes, Is.EqualTo(1));
        }));
    }

    [Test]
    public void RecoverableExtractionException_RefreshesOnceAndResetsGesture()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var extractionCalls = 0;
        var refreshes = new List<(TerminalExtractionResult? Result, Exception? Error)>();
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (_, _) =>
            {
                extractionCalls++;
                throw new InvalidOperationException("recoverable");
            },
            (result, error) => refreshes.Add((result, error)));

        StartSingleDrag(controller, fixture);
        Assert.That(controller.Release(Point(20, 20)), Is.True);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(extractionCalls, Is.EqualTo(1));
            Assert.That(refreshes, Has.Count.EqualTo(1));
            Assert.That(refreshes[0].Result, Is.Null);
            Assert.That(refreshes[0].Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Idle));
            Assert.That(controller.OwnsGesture, Is.False);
        }));
    }

    [Test]
    public void CancelAndInvalidatedSession_ClearSelectionAndPreventOldRefresh()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var session = true;
        var extractionCalls = 0;
        var refreshes = 0;
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (_, _) =>
            {
                extractionCalls++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 1, 1, "done");
            },
            (_, _) => refreshes++,
            () => session);

        Assert.That(controller.Press(Point(0, 0), TerminalDragRegion.Content, null, fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(24, 12));
        session = false;
        controller.Move(Point(25, 13));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Idle));
            Assert.That(controller.OwnsGesture, Is.False);
            Assert.That(controller.SelectedIds, Is.Empty);
            Assert.That(extractionCalls, Is.Zero);
            Assert.That(refreshes, Is.Zero);
        }));

        session = true;
        controller.Cancel();
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Cancel();
        Assert.That(controller.SelectedIds, Is.Empty);
    }

    [Test]
    public void ReleaseTwice_SubmitsOnlyOnceAndDoesNotRefreshOldWindow()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var extractionCalls = 0;
        var refreshes = 0;
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (batch, _) =>
            {
                extractionCalls++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success,
                    batch.RequestedPieces, batch.RequestedPieces, "done");
            },
            (_, _) => refreshes++);

        StartSingleDrag(controller, fixture);
        Assert.That(controller.Release(Point(20, 20)), Is.True);
        Assert.That(controller.Release(Point(20, 20)), Is.False);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(extractionCalls, Is.EqualTo(1));
            Assert.That(refreshes, Is.EqualTo(1));
        }));
    }

    [Test]
    public void WaitingPress_AllowsUnityPrimaryClickBeforeNativeRelease()
    {
        var fixture = CreateFixture();
        var transfers = 0;
        var controller = CreateController(fixture, OpaqueTarget(),
            (_, _, _, _, _) => null,
            (_, _) =>
            {
                transfers++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 1, 1, "done");
            });
        Assert.That(controller.AllowsPrimaryClick, Is.False);
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 4), Is.True);
        controller.Move(Point(6, 6));
        Assert.That(controller.OwnsGesture, Is.True);
        Assert.That(controller.AllowsPrimaryClick, Is.True, "Owning the press does not make it a drag.");
        Assert.That(controller.Release(Point(6, 6)), Is.False, "A normal release belongs to the existing click path.");
        Assert.That(controller.OwnsGesture, Is.False);
        Assert.That(controller.AllowsPrimaryClick, Is.False);
        Assert.That(transfers, Is.Zero, "The controller must not perform the click extraction itself.");
    }

    [Test]
    public void ConfirmedDrag_DisallowsPrimaryClickBeforeAndAfterRelease()
    {
        var fixture = CreateFixture();
        var transfers = 0;
        var controller = CreateController(fixture, OpaqueTarget(),
            (shown, current, ids, revision, amount) => fixture.Menu.CreateDragBatch(shown, current, ids, revision, amount),
            (batch, _) =>
            {
                transfers++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, batch.RequestedPieces,
                    batch.RequestedPieces, "done");
            });
        StartSingleDrag(controller, fixture);
        Assert.That(controller.AllowsPrimaryClick, Is.False);
        Assert.That(controller.Release(Point(20, 20)), Is.True);
        Assert.That(controller.AllowsPrimaryClick, Is.False);
        Assert.That(controller.Release(Point(20, 20)), Is.False);
        Assert.That(transfers, Is.EqualTo(1));
    }

    [Test]
    public void WaitingPress_InvalidSessionDoesNotAllowPrimaryClick()
    {
        var fixture = CreateFixture();
        var active = true;
        var controller = CreateController(fixture, OpaqueTarget(), (_, _, _, _, _) => null,
            (_, _) => TerminalExtractionResult.Create(TerminalExtractionStatus.Failed, 1, 0, "unused"),
            session: () => active);
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 4), Is.True);
        active = false;
        Assert.That(controller.AllowsPrimaryClick, Is.False);
        Assert.That(controller.Release(Point(5, 5)), Is.True, "An invalidated owned press must discard its release.");
    }

    [Test]
    public void ThresholdAndPointerMoves_DoNotResolveOrExtractBeforeDragStarts()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var resolveCalls = 0;
        var extractionCalls = 0;
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (_, _) =>
            {
                extractionCalls++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 1, 1, "done");
            },
            (_, _) => { },
            resolve: (PointerPoint _, out InventoryDropTarget? resolved, out TerminalDragTargetState state) =>
            {
                resolveCalls++;
                resolved = target;
                state = TerminalDragTargetState.Pending;
                return true;
            });

        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 4), Is.True);
        controller.Move(Point(6, 6));
        controller.Move(Point(7, 7));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Waiting));
            Assert.That(resolveCalls, Is.Zero);
            Assert.That(extractionCalls, Is.Zero);
        }));
        controller.Move(Point(8, 8));
        Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.DraggingSingle));
        Assert.That(resolveCalls, Is.EqualTo(1));
        Assert.That(extractionCalls, Is.Zero);
    }

    [TestCase(TerminalDragTargetState.None, false)]
    [TestCase(TerminalDragTargetState.Rejected, false)]
    [TestCase(TerminalDragTargetState.Pending, true)]
    public void ReleaseTargetState_SubmitsOnlyPending(TerminalDragTargetState state, bool shouldSubmit)
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var extractionCalls = 0;
        var refreshes = 0;
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (batch, _) =>
            {
                extractionCalls++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success,
                    batch.RequestedPieces, batch.RequestedPieces, "done");
            },
            (_, _) => refreshes++,
            resolve: (PointerPoint _, out InventoryDropTarget? resolved, out TerminalDragTargetState resolvedState) =>
            {
                resolved = state == TerminalDragTargetState.Pending ? target : null;
                resolvedState = state;
                return state != TerminalDragTargetState.None;
            });

        StartSingleDrag(controller, fixture);
        controller.Release(Point(20, 20));

        Assert.That(extractionCalls, Is.EqualTo(shouldSubmit ? 1 : 0));
        Assert.That(refreshes, Is.EqualTo(shouldSubmit ? 1 : 0));
    }

    [Test]
    public void Release_ReparsesTargetAndDoesNotSubmitAfterHoverBecomesRejected()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var resolveCalls = 0;
        var extractionCalls = 0;
        var refreshes = 0;
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (batch, _) =>
            {
                extractionCalls++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success,
                    batch.RequestedPieces, batch.RequestedPieces, "done");
            },
            (_, _) => refreshes++,
            resolve: (PointerPoint _, out InventoryDropTarget? resolved, out TerminalDragTargetState state) =>
            {
                resolveCalls++;
                resolved = resolveCalls == 1 ? target : null;
                state = resolveCalls == 1 ? TerminalDragTargetState.Pending : TerminalDragTargetState.Rejected;
                return resolveCalls == 1;
            });

        StartSingleDrag(controller, fixture);
        controller.Release(Point(20, 20));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(resolveCalls, Is.EqualTo(2));
            Assert.That(extractionCalls, Is.Zero);
            Assert.That(refreshes, Is.Zero);
        }));
    }

    [Test]
    public void SourcePageInvalidation_PreventsTransferAndRefresh()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var currentPage = fixture.Page;
        var extractionCalls = 0;
        var refreshes = 0;
        var replacementScreen = new StorageScreen(columns: 2, rows: 1);
        replacementScreen.SetEntriesInOrder(Array.Empty<DisplayEntry>());
        var replacementPage = replacementScreen.GetPage(0);
        var controller = CreateController(fixture, target,
            (shown, current, selected, revision, requested) => fixture.Menu.CreateDragBatch(
                shown, current, selected, revision, requested),
            (_, _) =>
            {
                extractionCalls++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, 1, 1, "done");
            },
            (_, _) => refreshes++,
            currentPage: () => currentPage,
            presentationRevision: () => currentPage.PresentationRevision);

        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(20, 20));
        currentPage = replacementPage;
        Assert.That(controller.Release(Point(20, 20)), Is.True);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(extractionCalls, Is.Zero);
            Assert.That(refreshes, Is.Zero);
            Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Idle));
        }));
    }

    [Test]
    public void Regions_OnlyContentAndSlotStartControllerGesture()
    {
        var fixture = CreateFixture();
        var target = OpaqueTarget();
        var controller = CreateController(fixture, target,
            (_, _, _, _, _) => null,
            (_, _) => TerminalExtractionResult.Create(TerminalExtractionStatus.Failed, 0, 0, "unused"));

        var nonInteractiveRegions = new[]
        {
            "title", "top-border", "bottom-border", "left-border", "right-border", "top-left-corner",
            "top-right-corner", "bottom-left-corner", "bottom-right-corner", "content-padding", "grid-gap",
            "non-interactive-text", "independent-control", "other-window"
        };
        foreach (var region in nonInteractiveRegions)
        {
            Assert.That(controller.Press(Point(1, 1), TerminalDragRegion.Other, null, fixture.Page,
                fixture.Candidates, 3), Is.False, region);
        }

        Assert.That(controller.Press(Point(1, 1), TerminalDragRegion.Content, null, fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Cancel();
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
    }

    [Test]
    public void PressedRegion_RemainsOwnedWhenPointerCrossesWindowBoundary()
    {
        var fixture = CreateFixture();
        var controller = CreateController(fixture, OpaqueTarget(),
            (_, _, _, _, _) => null,
            (_, _) => TerminalExtractionResult.Create(TerminalExtractionStatus.Failed, 0, 0, "unused"));

        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(-100, -100));

        Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.DraggingSingle));
        Assert.That(controller.OwnsGesture, Is.True);
    }

    [Test]
    public void GestureArea_OnlyRestrictsPress_OutsideReleaseSubmitsOnce()
    {
        var fixture = CreateFixture();
        var submissions = 0;
        var session = true;
        var controller = CreateController(fixture, OpaqueTarget(),
            (shown, current, ids, revision, amount) => fixture.Menu.CreateDragBatch(shown, current, ids, revision, amount),
            (batch, _) =>
            {
                submissions++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, batch.RequestedPieces,
                    batch.RequestedPieces, "done");
            }, session: () => session, gestureArea: point => point.X >= 0 && point.X <= 30);
        Assert.That(controller.Press(Point(-1, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.False);
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(40, 5));
        controller.Wheel(-1, Point(40, 5));
        controller.Halve(Point(40, 5));
        Assert.That(controller.Release(Point(40, 5)), Is.True);
        Assert.That(controller.Release(Point(40, 5)), Is.False);
        Assert.That(submissions, Is.EqualTo(1));
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(40, 5));
        session = false;
        controller.Move(Point(41, 5));
        Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Idle));
        Assert.That(submissions, Is.EqualTo(1));
    }

    [Test]
    public void Release_UsesNewlyResolvedAnchorRatherThanHoverCell()
    {
        var fixture = CreateFixture();
        var inventory = OpaqueTarget().Inventory;
        var hover = new InventoryDropTarget(inventory, new GridDropAnchor(1, 1));
        var released = new InventoryDropTarget(inventory, new GridDropAnchor(4, 2));
        InventoryDropTarget? submitted = null;
        var resolutions = 0;
        var submissions = 0;
        var refreshes = 0;
        var controller = CreateController(fixture, hover,
            (shown, current, ids, revision, amount) => fixture.Menu.CreateDragBatch(shown, current, ids, revision, amount),
            (batch, target) =>
            {
                submitted = target;
                submissions++;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, batch.RequestedPieces,
                    batch.RequestedPieces, "done");
            }, (_, _) => refreshes++,
            resolve: (PointerPoint _, out InventoryDropTarget? target, out TerminalDragTargetState state) =>
            {
                target = ++resolutions == 1 ? hover : released;
                state = TerminalDragTargetState.Pending;
                return true;
            });
        StartSingleDrag(controller, fixture);
        Assert.That(submissions, Is.Zero, "A hover marker is not an extraction.");
        controller.Release(Point(90, 90));
        controller.Release(Point(90, 90));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(resolutions, Is.EqualTo(2));
            Assert.That(submitted, Is.SameAs(released));
            Assert.That(submitted!.Anchor, Is.EqualTo(new GridDropAnchor(4, 2)));
            Assert.That(submissions, Is.EqualTo(1));
            Assert.That(refreshes, Is.EqualTo(1));
        }));
    }

    [Test]
    public void ProjectedRevision_RejectsUnappliedPipelineChangeAndAllowsNewGestureAfterRegroup()
    {
        var fixture = CreateFixture();
        var screen = new StorageScreen(2, 1);
        screen.SetEntriesInOrder(fixture.Menu.GetEntries());
        long pipelineRevision = 4;
        long projectedRevision = 4;
        var source = screen.GetPage(projectedRevision);
        var controller = CreateController(fixture, OpaqueTarget(),
            (shown, current, ids, revision, amount) => fixture.Menu.CreateDragBatch(shown, current, ids, revision, amount),
            (_, _) => TerminalExtractionResult.Create(TerminalExtractionStatus.Failed, 0, 0, "unused"),
            currentPage: () => screen.GetPage(projectedRevision), presentationRevision: () => pipelineRevision);
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", source,
            fixture.Candidates, 3), Is.True);
        pipelineRevision = 5;
        controller.SynchronizePage();
        Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Idle));
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", screen.GetPage(projectedRevision),
            fixture.Candidates, 3), Is.False, "An unapplied pipeline must not relabel old groups.");
        projectedRevision = pipelineRevision;
        screen.InvalidatePage();
        screen.UpdateEntriesInOrder(fixture.Menu.GetEntries());
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", screen.GetPage(projectedRevision),
            fixture.Candidates, 3), Is.True);
        Assert.That(controller.AllowsPrimaryClick, Is.True);
    }

    [Test]
    public void QuantityRefresh_PreservesRetainedSelectionAndUsesUpdatedBatchTotals()
    {
        var fixture = CreateFixture();
        var screen = new StorageScreen(2, 1);
        screen.SetEntriesInOrder(fixture.Menu.GetEntries());
        var source = screen.GetPage(0);
        long moved = 0;
        var controller = CreateController(fixture, OpaqueTarget(),
            (shown, current, ids, revision, amount) => fixture.Menu.CreateDragBatch(shown, current, ids, revision, amount),
            (batch, _) =>
            {
                moved += batch.RequestedPieces;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, batch.RequestedPieces,
                    batch.RequestedPieces, "done");
            }, currentPage: () => screen.GetPage(0));
        SelectBoth(controller, fixture);
        UpdateFruitQuantity(fixture, screen, 3);
        Assert.That(screen.GetPage(0).ViewRevision, Is.EqualTo(source.ViewRevision));
        controller.SynchronizePage();
        Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Selected));
        Assert.That(controller.SelectedIds, Has.Count.EqualTo(2));
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", screen.GetPage(0),
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(20, 20));
        Assert.That(controller.Release(Point(20, 20)), Is.True);
        Assert.That(moved, Is.EqualTo(12));
    }

    [Test]
    public void QuantityRefresh_PreservesActiveDragAndClampsRequestToLatestTotal()
    {
        var fixture = CreateFixture();
        var screen = new StorageScreen(2, 1);
        screen.SetEntriesInOrder(fixture.Menu.GetEntries());
        long moved = 0;
        var controller = CreateController(fixture, OpaqueTarget(),
            (shown, current, ids, revision, amount) => fixture.Menu.CreateDragBatch(shown, current, ids, revision, amount),
            (batch, _) =>
            {
                moved = batch.RequestedPieces;
                return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, moved, moved, "done");
            }, currentPage: () => screen.GetPage(0));
        StartSingleDrag(controller, fixture);
        UpdateFruitQuantity(fixture, screen, 1);
        controller.SynchronizePage();
        Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.DraggingSingle));
        Assert.That(controller.OwnsGesture, Is.True);
        Assert.That(controller.Release(Point(20, 20)), Is.True);
        Assert.That(moved, Is.EqualTo(6));
    }

    [Test]
    public void MappingRefresh_CancelsRetainedSelectionBeforeAnotherPress()
    {
        var fixture = CreateFixture();
        var screen = new StorageScreen(2, 1);
        screen.SetEntriesInOrder(fixture.Menu.GetEntries());
        var controller = CreateController(fixture, OpaqueTarget(), (_, _, _, _, _) => null,
            (_, _) => TerminalExtractionResult.Create(TerminalExtractionStatus.Failed, 0, 0, "unused"),
            currentPage: () => screen.GetPage(0));
        SelectBoth(controller, fixture);
        screen.UpdateEntriesInOrder(fixture.Menu.GetEntries().Reverse());
        controller.SynchronizePage();
        Assert.That(controller.Phase, Is.EqualTo(TerminalDragPhase.Idle));
        Assert.That(controller.SelectedIds, Is.Empty);
    }

    private static void UpdateFruitQuantity(Fixture fixture, StorageScreen screen, long firstAmount)
    {
        var entries = fixture.Menu.GetEntries().SelectMany(group => group.Members).Select(entry =>
            new TerminalEntry(entry.Key, entry.Serial == 1 ? firstAmount : entry.Amount, entry.DisplayName,
                entry.SpriteKey, entry.SpriteWidth, entry.SpriteHeight, entry.Serial, entry.Document, entry.TotalUnits));
        fixture.Menu.BroadcastChanges(new TerminalInventorySnapshot(true, entries.ToArray()));
        screen.UpdateEntriesInOrder(fixture.Menu.GetEntries());
    }

    private static void SelectBoth(TerminalDragController controller, Fixture fixture)
    {
        Assert.That(controller.Press(Point(0, 0), TerminalDragRegion.Content, null, fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(24, 12));
        Assert.That(controller.Release(Point(24, 12)), Is.True);
    }

    private static void StartSingleDrag(TerminalDragController controller, Fixture fixture)
    {
        Assert.That(controller.Press(Point(5, 5), TerminalDragRegion.Slot, "group-fruit", fixture.Page,
            fixture.Candidates, 3), Is.True);
        controller.Move(Point(20, 20));
    }

    private static TerminalDragController CreateController(Fixture fixture, InventoryDropTarget target,
        Func<StorageScreenPage, StorageScreenPage, IEnumerable<string>, long, long?, TerminalDragBatch?> createBatch,
        Func<TerminalDragBatch, InventoryDropTarget, TerminalExtractionResult> extract,
        Action<TerminalExtractionResult?, Exception?>? refresh = null,
        Func<bool>? session = null,
        Func<StorageScreenPage>? currentPage = null,
        Func<long>? presentationRevision = null,
        TerminalDragResolveTarget? resolve = null,
        Func<PointerPoint, bool>? gestureArea = null)
    {
        return new TerminalDragController(
            session ?? (() => true),
            currentPage ?? (() => fixture.Page),
            presentationRevision ?? (() => fixture.Page.PresentationRevision),
            createBatch,
            resolve ?? ((PointerPoint _, out InventoryDropTarget? resolved, out TerminalDragTargetState state) =>
            {
                resolved = target;
                state = TerminalDragTargetState.Pending;
                return true;
            }),
            extract,
            refresh ?? ((_, _) => { }),
            _ => { },
            _ => { },
            (_, _) => { },
            () => { },
            (_, _) => { },
            gestureArea);
    }

    private static Fixture CreateFixture()
    {
        var first = new TerminalEntry(new ItemKey("fruit", new Dictionary<string, string> { ["variant"] = "one" }),
            6, "Fruit one", "fruit", serial: 1);
        var second = new TerminalEntry(new ItemKey("fruit", new Dictionary<string, string> { ["variant"] = "two" }),
            5, "Fruit two", "fruit", serial: 2);
        var stone = new TerminalEntry(new ItemKey("stone"), 4, "Stone", "stone", serial: 3);
        var menu = new StorageMenu();
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[] { first, second, stone }));
        menu.SetProjection(entries => new[]
        {
            new ProjectedGroup("group-fruit", entries.Where(entry => entry.Key.Identifier == "fruit").ToArray()),
            new ProjectedGroup("group-stone", entries.Where(entry => entry.Key.Identifier == "stone").ToArray())
        });

        var screen = new StorageScreen(columns: 2, rows: 1);
        screen.SetEntriesInOrder(menu.GetEntries());
        var page = screen.GetPage(0);
        var candidates = new[]
        {
            new DragSelectionCandidate<string>("group-fruit", new PointerRectangle(0, 0, 10, 10), 0),
            new DragSelectionCandidate<string>("group-stone", new PointerRectangle(12, 0, 22, 10), 1)
        };
        var groups = menu.GetEntries();
        var expected = groups.SelectMany(group => group.Members).Select(member => member.Key).ToArray();
        return new Fixture(menu, page, candidates, expected);
    }

    private static PointerPoint Point(float x, float y) => new(x, y);

    private static InventoryDropTarget OpaqueTarget() =>
        new((GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory)));

    private sealed class Fixture
    {
        internal Fixture(StorageMenu menu, StorageScreenPage page,
            IReadOnlyList<DragSelectionCandidate<string>> candidates, IReadOnlyList<ItemKey> expectedStepKeys)
        {
            Menu = menu;
            Page = page;
            Candidates = candidates;
            ExpectedStepKeys = expectedStepKeys;
        }

        internal StorageMenu Menu { get; }
        internal StorageScreenPage Page { get; }
        internal IReadOnlyList<DragSelectionCandidate<string>> Candidates { get; }
        internal IReadOnlyList<ItemKey> ExpectedStepKeys { get; }
    }
}
