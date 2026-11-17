using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Interaction;

[TestFixture]
public sealed class DragSelectionModelTests
{
    private static readonly IReadOnlyList<DragSelectionCandidate<string>> Candidates = new[]
    {
        new DragSelectionCandidate<string>("second", new PointerRectangle(20, 0, 30, 10), 2),
        new DragSelectionCandidate<string>("first", new PointerRectangle(0, 0, 10, 10), 1),
        new DragSelectionCandidate<string>("edge", new PointerRectangle(30, 0, 40, 10), 3)
    };

    [Test]
    public void Update_ReturnsIntersectingIdsInReadingOrderForEitherDragDirection()
    {
        var model = new DragSelectionModel<string>();
        Assert.That(model.Begin(new PointerPoint(28, 12), 4), Is.True);

        var selected = model.Update(new PointerPoint(-2, -2), Candidates, 4);

        Assert.That(selected, Is.EqualTo(new[] { "first", "second" }));
        Assert.That(model.Complete(4), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(model.IsActive, Is.False);
    }

    [Test]
    public void Update_RejectsZeroAreaAndEdgeOnlySelections()
    {
        var model = new DragSelectionModel<string>();
        model.Begin(new PointerPoint(10, 10), 1);
        Assert.That(model.Update(new PointerPoint(10, 10), Candidates, 1), Is.Empty);

        model.Begin(new PointerPoint(10, 0), 1);
        Assert.That(model.Update(new PointerPoint(20, 10), Candidates, 1), Is.Empty);
    }

    [Test]
    public void Update_CancelsOnDuplicateIdsOrRevisionChange()
    {
        var model = new DragSelectionModel<string>();
        model.Begin(new PointerPoint(0, 0), 7);
        var duplicate = new[]
        {
            new DragSelectionCandidate<string>("same", new PointerRectangle(0, 0, 2, 2), 1),
            new DragSelectionCandidate<string>("same", new PointerRectangle(3, 3, 5, 5), 2)
        };
        Assert.That(model.Update(new PointerPoint(5, 5), duplicate, 7), Is.Empty);
        Assert.That(model.IsActive, Is.False);

        model.Begin(new PointerPoint(0, 0), 7);
        Assert.That(model.Update(new PointerPoint(5, 5), Candidates, 8), Is.Empty);
        Assert.That(model.Complete(7), Is.Empty);
    }

    [Test]
    public void Update_NullCandidatesCancelsPreviouslySelectedIds()
    {
        var model = new DragSelectionModel<string>();
        model.Begin(new PointerPoint(0, 0), 3);
        Assert.That(model.Update(new PointerPoint(8, 8), Candidates, 3), Is.EqualTo(new[] { "first" }));

        Assert.That(model.Update(new PointerPoint(8, 8), null!, 3), Is.Empty);
        Assert.That(model.IsActive, Is.False);
        Assert.That(model.SelectedIds, Is.Empty);
        Assert.That(model.Complete(3), Is.Empty);
    }

    [Test]
    public void Cancel_ClearsSelectionAndCompleteRejectsStaleRevision()
    {
        var model = new DragSelectionModel<string>();
        model.Begin(new PointerPoint(0, 0), 3);
        model.Update(new PointerPoint(8, 8), Candidates, 3);
        model.Cancel();
        Assert.That(model.SelectedIds, Is.Empty);

        model.Begin(new PointerPoint(0, 0), 3);
        model.Update(new PointerPoint(8, 8), Candidates, 3);
        Assert.That(model.Complete(4), Is.Empty);
        Assert.That(model.SelectedIds, Is.Empty);
    }
}
