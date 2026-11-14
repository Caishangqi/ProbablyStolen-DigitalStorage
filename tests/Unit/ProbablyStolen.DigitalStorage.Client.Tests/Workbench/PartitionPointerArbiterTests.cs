using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Workbench;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Workbench;

[TestFixture]
public sealed class PartitionPointerArbiterTests
{
    [Test]
    public void LeftClick_ProducesConfigureOnceAndRejectsReuse()
    {
        var arbiter = new PartitionPointerArbiter();
        Assert.That(arbiter.Press(PartitionPointerButton.Left, 2, "rule-a", 4, true, true, false, false), Is.True);

        Assert.That(arbiter.Release(PartitionPointerButton.Left, 2, "rule-a", 4, true, true, false, false),
            Is.EqualTo(PartitionPointerGesture.Configure));
        Assert.That(arbiter.Release(PartitionPointerButton.Left, 2, "rule-a", 4, true, true, false, false),
            Is.EqualTo(PartitionPointerGesture.None));
    }

    [Test]
    public void RightAndMiddleClicks_UseIdentityAndGeneration()
    {
        var arbiter = new PartitionPointerArbiter();
        arbiter.Press(PartitionPointerButton.Right, 1, "rule-a", 7, true, true, false, false);
        Assert.That(arbiter.Release(PartitionPointerButton.Right, 1, "rule-b", 7, true, true, false, false),
            Is.EqualTo(PartitionPointerGesture.None));

        arbiter.Press(PartitionPointerButton.Middle, 1, "rule-a", 7, true, true, false, false);
        Assert.That(arbiter.Release(PartitionPointerButton.Middle, 1, "rule-a", 8, true, true, false, false),
            Is.EqualTo(PartitionPointerGesture.None));

        arbiter.Press(PartitionPointerButton.Middle, 1, "rule-a", 7, true, true, false, false);
        Assert.That(arbiter.Release(PartitionPointerButton.Middle, 1, "rule-a", 7, true, true, false, false),
            Is.EqualTo(PartitionPointerGesture.Duplicate));
    }

    [Test]
    public void LeftDrag_ProducesMoveAndNeverConfigure()
    {
        var arbiter = new PartitionPointerArbiter();
        arbiter.Press(PartitionPointerButton.Left, 1, "rule-a", 1, true, true, false, false);
        arbiter.Move(4);

        Assert.That(arbiter.Release(PartitionPointerButton.Left, 4, "rule-a", 1, true, true, false, false),
            Is.EqualTo(PartitionPointerGesture.Move));
    }

    [Test]
    public void Press_RefusesDisabledHiddenSessionNativeDragAndConflictingButtons()
    {
        var arbiter = new PartitionPointerArbiter();
        Assert.That(arbiter.Press(PartitionPointerButton.Left, 1, null, 0, true, true, false, false), Is.False);
        Assert.That(arbiter.Press(PartitionPointerButton.Left, 1, "rule-a", 0, false, true, false, false), Is.False);
        Assert.That(arbiter.Press(PartitionPointerButton.Left, 1, "rule-a", 0, true, false, false, false), Is.False);
        Assert.That(arbiter.Press(PartitionPointerButton.Left, 1, "rule-a", 0, true, true, true, false), Is.False);
        Assert.That(arbiter.Press(PartitionPointerButton.Left, 1, "rule-a", 0, true, true, false, true), Is.False);
    }

    [Test]
    public void Cancel_DropsThePressBeforeAReusedSlotCanReceiveIt()
    {
        var arbiter = new PartitionPointerArbiter();
        arbiter.Press(PartitionPointerButton.Right, 3, "old-rule", 2, true, true, false, false);
        arbiter.Cancel();

        Assert.That(arbiter.Release(PartitionPointerButton.Right, 3, "new-rule", 2, true, true, false, false),
            Is.EqualTo(PartitionPointerGesture.None));
    }

    [TestCase(false, true, false, false)]
    [TestCase(true, false, false, false)]
    [TestCase(true, true, true, false)]
    [TestCase(true, true, false, true)]
    public void InvalidVisibilitySessionDragOrConflict_Cancels(bool visible, bool session, bool nativeDrag,
        bool conflict)
    {
        var arbiter = new PartitionPointerArbiter();
        Assert.That(arbiter.Press(PartitionPointerButton.Left, 1, "rule-a", 1, true, true, false, false), Is.True);
        arbiter.Move(2);

        Assert.That(arbiter.Release(PartitionPointerButton.Left, 2, "rule-a", 1, visible, session, nativeDrag, conflict),
            Is.EqualTo(PartitionPointerGesture.None));
        Assert.That(arbiter.IsPressed, Is.False);
    }
}
