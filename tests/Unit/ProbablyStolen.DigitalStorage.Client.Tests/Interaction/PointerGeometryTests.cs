using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Interaction;

[TestFixture]
public sealed class PointerGeometryTests
{
    [Test]
    public void TryCreate_NormalizesBothDragDirections()
    {
        Assert.That(PointerRectangle.TryCreate(new PointerPoint(10, 20), new PointerPoint(30, 40), out var forward), Is.True);
        Assert.That(PointerRectangle.TryCreate(new PointerPoint(30, 40), new PointerPoint(10, 20), out var reverse), Is.True);
        Assert.That(forward, Is.EqualTo(reverse));
        Assert.That(forward, Is.EqualTo(new PointerRectangle(10, 20, 30, 40)));
    }

    [Test]
    public void Intersects_RequiresPositiveAreaAndRejectsEdgeContact()
    {
        var selection = new PointerRectangle(0, 0, 10, 10);
        Assert.That(selection.Intersects(new PointerRectangle(9, 9, 20, 20)), Is.True);
        Assert.That(selection.Intersects(new PointerRectangle(10, 0, 20, 10)), Is.False);
        Assert.That(selection.Intersects(new PointerRectangle(0, 10, 10, 20)), Is.False);
        Assert.That(selection.Intersects(new PointerRectangle(0, 0, 0, 10)), Is.False);
    }

    [Test]
    public void TryCreate_RejectsNonFiniteCoordinatesAndPreservesInvalidSignal()
    {
        var invalid = new PointerPoint(float.NaN, 1);
        Assert.That(invalid.IsValid, Is.False);
        Assert.That(PointerRectangle.TryCreate(invalid, new PointerPoint(1, 1), out _), Is.False);
        Assert.That(new PointerRectangle(float.PositiveInfinity, 0, 1, 1).IsValid, Is.False);
    }

    [Test]
    public void Gesture_ReportsNonFiniteInputAsInvalid()
    {
        var gesture = new PointerGesture(PointerGesturePhase.Move, PointerButton.Primary,
            new PointerPoint(1, 2), float.NaN);
        Assert.That(gesture.IsValid, Is.False);
        Assert.That(new PointerGesture(PointerGesturePhase.Move, PointerButton.Primary, new PointerPoint(1, 2)).IsValid,
            Is.True);
    }
}
