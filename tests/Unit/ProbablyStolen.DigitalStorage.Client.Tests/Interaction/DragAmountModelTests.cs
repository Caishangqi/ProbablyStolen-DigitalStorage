using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Interaction;

[TestFixture]
public sealed class DragAmountModelTests
{
    [Test]
    public void Begin_UsesAllAvailablePiecesAndHalveRoundsUp()
    {
        var model = new DragAmountModel();
        Assert.That(model.Begin(89), Is.True);
        Assert.That(model.RequestedPieces, Is.EqualTo(89));
        Assert.That(model.Halve(89), Is.EqualTo(45));

        Assert.That(model.Begin(11), Is.True);
        Assert.That(model.Halve(11), Is.EqualTo(6));
    }

    [Test]
    public void Wheel_AdjustsOnePiecePerStepAndClampsToLiveAvailability()
    {
        var model = new DragAmountModel();
        model.Begin(5);
        Assert.That(model.ApplyWheel(-1, 5), Is.EqualTo(4));
        Assert.That(model.ApplyWheel(-10, 5), Is.EqualTo(1));
        Assert.That(model.ApplyWheel(100, 5), Is.EqualTo(5));
        Assert.That(model.ApplyWheel(0, 3), Is.EqualTo(3));
    }

    [Test]
    public void Wheel_DoesNotOverflowAtLongMaximum()
    {
        var model = new DragAmountModel();
        model.Begin(long.MaxValue);
        Assert.That(model.ApplyWheel(1, long.MaxValue), Is.EqualTo(long.MaxValue));
        Assert.That(model.Halve(long.MaxValue), Is.EqualTo(long.MaxValue / 2 + 1));
    }

    [Test]
    public void ZeroAvailability_EndsTheDragAndRejectsEmptyStarts()
    {
        var model = new DragAmountModel();
        Assert.That(model.Begin(0), Is.False);
        Assert.That(model.Begin(-1), Is.False);
        Assert.That(model.ApplyWheel(1, 0), Is.Zero);
        Assert.That(model.IsActive, Is.False);
    }

    [Test]
    public void End_ResetsTheRequestSoNextDragStartsAtCurrentFullCount()
    {
        var model = new DragAmountModel();
        model.Begin(10);
        model.Halve(10);
        model.End();
        Assert.That(model.RequestedPieces, Is.Zero);
        Assert.That(model.Begin(7), Is.True);
        Assert.That(model.RequestedPieces, Is.EqualTo(7));
    }
}
