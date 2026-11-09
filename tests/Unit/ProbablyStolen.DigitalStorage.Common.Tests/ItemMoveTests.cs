using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class ItemMoveTests
{
    [Test]
    public void Run_ValidMove_ReachesTarget()
    {
        var port = new FakePort();
        var result = ItemMove.Run(port);
        Assert.That(result.Outcome, Is.EqualTo(ItemMoveOutcome.Moved));
        Assert.That(port.ReturnCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Run_DetachThrows_ReturnsOnlyWhenDetached(bool afterDetach)
    {
        var port = new FakePort { DetachError = true, DetachErrorAfterRemoval = afterDetach };
        var result = ItemMove.Run(port);
        Assert.That(result.Outcome, Is.EqualTo(afterDetach ? ItemMoveOutcome.Returned : ItemMoveOutcome.Unchanged));
        Assert.That(result.Error, Is.InstanceOf<InvalidOperationException>());
        Assert.That(port.ReturnCalls, Is.EqualTo(afterDetach ? 1 : 0));
    }

    [Test]
    public void Run_DetachRefused_LeavesSourceUntouched()
    {
        var port = new FakePort { DetachRefused = true };
        var result = ItemMove.Run(port);
        Assert.That(result.Outcome, Is.EqualTo(ItemMoveOutcome.Unchanged));
        Assert.That(port.ReturnCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Run_AttachFails_AttemptsOneReturn(bool throws)
    {
        var port = new FakePort { AttachRefused = true, AttachError = throws };
        var result = ItemMove.Run(port);
        Assert.That(result.Outcome, Is.EqualTo(ItemMoveOutcome.Returned));
        Assert.That(port.ReturnCalls, Is.EqualTo(1));
        Assert.That(port.Location, Is.EqualTo("source"));
    }

    [Test]
    public void Run_AttachThrowsAfterAcceptance_DoesNotReturnAgain()
    {
        var port = new FakePort { AttachErrorAfterAcceptance = true };
        var result = ItemMove.Run(port);
        Assert.That(result.Outcome, Is.EqualTo(ItemMoveOutcome.Moved));
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(port.ReturnCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Run_SourceRejectsOrThrows_StopsAfterOneReturn(bool throws)
    {
        var port = new FakePort { AttachRefused = true, ReturnRefused = true, ReturnError = throws };
        var result = ItemMove.Run(port);
        Assert.That(result.Outcome, Is.EqualTo(ItemMoveOutcome.Unowned));
        Assert.That(port.ReturnCalls, Is.EqualTo(1));
        Assert.That(port.Location, Is.Null);
    }

    [Test]
    public void Recover_PartialMerge_ReturnsRemainderOnce()
    {
        var port = new FakePort { Location = null, Units = 4 };
        var result = ItemMove.Recover(port);
        Assert.That(result.Outcome, Is.EqualTo(ItemMoveOutcome.Returned));
        Assert.That(port.Units, Is.EqualTo(4));
        Assert.That(port.ReturnCalls, Is.EqualTo(1));
    }

    [TestCase("source")]
    [TestCase("target")]
    public void Recover_AlreadyOwned_DoesNotClaimReturn(string owner)
    {
        var port = new FakePort { Location = owner };
        var error = new InvalidOperationException();
        var result = ItemMove.Recover(port, error);
        Assert.That(result.Outcome, Is.EqualTo(ItemMoveOutcome.AlreadySafe));
        Assert.That(result.Error, Is.SameAs(error));
        Assert.That(port.Location, Is.EqualTo(owner));
        Assert.That(port.ReturnCalls, Is.Zero);
    }

    [Test]
    public void Recover_FullyMerged_DoesNotReturn()
    {
        var port = new FakePort { Location = null, Units = 0 };
        var result = ItemMove.Recover(port);
        Assert.That(result.Outcome, Is.EqualTo(ItemMoveOutcome.AlreadySafe));
        Assert.That(port.ReturnCalls, Is.Zero);
    }

    [Test]
    public void RequireSafeDrag_Unowned_PropagatesError()
    {
        Assert.That((Action)(() => ItemMove.RequireSafeDrag(false)), Throws.InvalidOperationException);
        Assert.That((Action)(() => ItemMove.RequireSafeDrag(true)), Throws.Nothing);
    }

    private sealed class FakePort : IItemMovePort
    {
        internal bool DetachRefused { get; set; }
        internal bool DetachError { get; set; }
        internal bool DetachErrorAfterRemoval { get; set; }
        internal bool AttachRefused { get; set; }
        internal bool AttachError { get; set; }
        internal bool AttachErrorAfterAcceptance { get; set; }
        internal bool ReturnRefused { get; set; }
        internal bool ReturnError { get; set; }
        internal string? Location { get; set; } = "source";
        internal int Units { get; set; } = 10;
        internal int ReturnCalls { get; private set; }

        public bool Detach()
        {
            if (DetachError && !DetachErrorAfterRemoval) throw new InvalidOperationException();
            if (DetachRefused) return false;
            Location = null;
            if (DetachError) throw new InvalidOperationException();
            return true;
        }

        public void Attach()
        {
            if (AttachError) throw new InvalidOperationException();
            if (AttachRefused) return;
            Location = "target";
            if (AttachErrorAfterAcceptance) throw new InvalidOperationException();
        }

        public bool IsAtTarget() => Location == "target";
        public bool IsSafe() => Location != null || Units <= 0;
        public void ReturnToSource()
        {
            ReturnCalls++;
            if (ReturnError) throw new InvalidOperationException();
            if (!ReturnRefused) Location = "source";
        }
    }
}
