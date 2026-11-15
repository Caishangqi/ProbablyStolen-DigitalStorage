using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class ModuleBayTransactionTests
{
    private static readonly ModuleBayLayout Layout = new(1, 1);

    [Test]
    public void Execute_TenStagesThenFull_ConsumesExactlyTenTimes()
    {
        var transaction = new ModuleBayTransaction();
        var port = new MemoryPort();
        for (var stage = 0; stage < 10; stage++)
            Assert.That(transaction.Execute(port, stage, 10, Layout, true).Status, Is.EqualTo(ModuleBayChangeStatus.Changed));
        Assert.That(transaction.Execute(port, 10, 10, Layout, true).Status, Is.EqualTo(ModuleBayChangeStatus.Refused));
        Assert.That((port.Stage, port.Value, port.Consumptions), Is.EqualTo((10, 1000L, 10)));
    }

    [TestCase("apply")]
    [TestCase("validate")]
    [TestCase("value")]
    public void Execute_PreconsumeFailure_RestoresSnapshot(string failure)
    {
        var port = new MemoryPort { Failure = failure, Stage = 3, Value = 300, Shape = "old", TagExists = false };
        var result = new ModuleBayTransaction().Execute(port, 3, 10, Layout, true);
        Assert.That(result.Status, Is.EqualTo(ModuleBayChangeStatus.Unavailable));
        Assert.That((port.Stage, port.Value, port.Consumptions, port.Shape, port.TagExists), Is.EqualTo((3, 300L, 0, "old", false)));
    }

    [Test]
    public void Execute_RollbackFails_LocksTransaction()
    {
        var transaction = new ModuleBayTransaction();
        var port = new MemoryPort { Failure = "validate", FailRestore = true };
        transaction.Execute(port, 0, 10, Layout, true);
        Assert.That(transaction.IsLocked, Is.True);
        Assert.That(transaction.Execute(port, 0, 10, Layout, true).Status, Is.EqualTo(ModuleBayChangeStatus.Unavailable));
    }

    [Test]
    public void Execute_ConsumptionThrows_PreservesCommitAndLocks()
    {
        var transaction = new ModuleBayTransaction();
        var port = new MemoryPort { Failure = "consume" };
        var result = transaction.Execute(port, 0, 10, Layout, true);
        Assert.That(result.Status, Is.EqualTo(ModuleBayChangeStatus.ConsumptionUncertain));
        Assert.That((port.Stage, port.Value, port.Consumptions), Is.EqualTo((1, 100L, 1)));
        Assert.That(transaction.ConsumptionUncertain, Is.True);
        transaction.Execute(port, 1, 10, Layout, true);
        Assert.That(port.Consumptions, Is.EqualTo(1));
    }

    [Test]
    public void Execute_NotifyThrows_DoesNotUndoCommit()
    {
        var port = new MemoryPort { Failure = "notify" };
        Assert.That(new ModuleBayTransaction().Execute(port, 0, 10, Layout, true).Succeeded, Is.True);
        Assert.That((port.Stage, port.Value, port.Consumptions), Is.EqualTo((1, 100L, 1)));
    }

    [Test]
    public void Execute_LayoutOnly_DoesNotChangeValueOrConsume()
    {
        var port = new MemoryPort();
        Assert.That(new ModuleBayTransaction().Execute(port, 0, 10, Layout, false).Succeeded, Is.True);
        Assert.That((port.Stage, port.Value, port.Consumptions), Is.EqualTo((1, 0L, 0)));
    }

    [Test]
    public void Execute_Reentry_IsRefused()
    {
        var transaction = new ModuleBayTransaction();
        var port = new MemoryPort();
        ModuleBayChangeResult? nested = null;
        port.OnApply = () => nested = transaction.Execute(port, 0, 10, Layout, true);
        Assert.That(transaction.Execute(port, 0, 10, Layout, true).Succeeded, Is.True);
        Assert.That(nested!.Status, Is.EqualTo(ModuleBayChangeStatus.Unavailable));
        Assert.That(port.Consumptions, Is.EqualTo(1));
    }

    private sealed class MemoryPort : IModuleBayTransactionPort
    {
        internal int Stage;
        internal long Value;
        internal int Consumptions;
        internal string Shape = "base";
        internal bool TagExists;
        internal string? Failure;
        internal bool FailRestore;
        internal Action? OnApply;
        private int _stage;
        private long _value;
        private string _shape = "";
        private bool _tagExists;
        public void Capture() { _stage = Stage; _value = Value; _shape = Shape; _tagExists = TagExists; }
        public void Apply(ModuleBayLayout layout, int stage) { Stage = stage; Shape = string.Concat(layout.Rows); TagExists = true; OnApply?.Invoke(); Fail("apply"); }
        public void Validate() => Fail("validate");
        public void AddValue() { Value += 100; Fail("value"); }
        public void Restore() { if (FailRestore) throw new InvalidOperationException(); Stage = _stage; Value = _value; Shape = _shape; TagExists = _tagExists; }
        public void Consume() { Consumptions++; Fail("consume"); }
        public void Notify() => Fail("notify");
        private void Fail(string point) { if (Failure == point) throw new InvalidOperationException(); }
    }
}
