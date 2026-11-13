using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Workbench;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

public sealed class PartitionBackHandlerTests
{
    [Test]
    public void Handle_CombinedStates_ReturnsOneLayerAtATimeWithoutClosingWorkbench()
    {
        var menu = true;
        var detail = true;
        var editor = true;
        var workbench = true;
        var gestures = new GridGestureArbiter();
        gestures.Press(1);
        gestures.Move(2);
        var moves = 0;

        void Back() => PartitionBackHandler.Handle(editor, menu, gestures.PressSlot >= 0, detail,
            () => menu = false, gestures.Cancel, () => detail = false, () => editor = false);

        Back();
        Assert.That(menu, Is.False);
        Assert.That(gestures.IsDragging, Is.True);
        Back();
        if (gestures.Release(2, 1) == GridGesture.Drop) moves++;
        Assert.That(moves, Is.Zero);
        Assert.That(detail, Is.True);
        Back();
        Assert.That(detail, Is.False);
        Assert.That(editor, Is.True);
        Back();
        Assert.That(editor, Is.False);
        Back();
        Assert.That(workbench, Is.True);
    }
}
