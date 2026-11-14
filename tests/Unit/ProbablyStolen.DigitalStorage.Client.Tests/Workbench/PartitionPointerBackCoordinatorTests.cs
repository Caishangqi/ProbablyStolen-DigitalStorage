using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Workbench;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Workbench;

[TestFixture]
public sealed class PartitionPointerBackCoordinatorTests
{
    [TestCase(PartitionPointerButton.Left)]
    [TestCase(PartitionPointerButton.Right)]
    [TestCase(PartitionPointerButton.Middle)]
    public void ConsumedBack_CancelsBothPressTrackersBeforeRelease(PartitionPointerButton button)
    {
        var legacy = new GridGestureArbiter();
        var pointer = new PartitionPointerArbiter();
        var back = new PartitionPointerBackCoordinator(legacy, pointer);
        Assert.That(pointer.Press(button, 2, "rule-a", 7, true, true, false, false), Is.True);
        if (button == PartitionPointerButton.Left)
        {
            legacy.Press(2);
            legacy.Move(3);
        }

        Assert.That(back.Cancel(), Is.True);
        Assert.That(back.HasActivePress, Is.False);
        Assert.That(pointer.Release(button, 2, "rule-a", 7, true, true, false, false),
            Is.EqualTo(PartitionPointerGesture.None));
        Assert.That(legacy.Release(2, 1.0), Is.EqualTo(GridGesture.None));
    }

    [Test]
    public void BackWithoutPress_DoesNotConsumeTheNextWindowBackLayer()
    {
        var back = new PartitionPointerBackCoordinator(new GridGestureArbiter(), new PartitionPointerArbiter());

        Assert.That(back.Cancel(), Is.False);
        Assert.That(back.HasActivePress, Is.False);
    }
}
