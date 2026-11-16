using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Mod.Patches;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
[NonParallelizable]
public sealed class ControllerTooltipBridgeTests
{
    [Test]
    public void Begin_ProjectsDeepCopyAndRestoresWithoutChangingBaseState()
    {
        var item = Controller();
        var original = item.modifiedState;
        var baseState = item.state;
        original.GetTag("other").SetInt(7);
        var transfers = GraphUtils.NativeTransfers.Count;
        using var registration = Register(3);
        using (var scope = ControllerTooltipBridge.Begin(item))
        {
            Assert.That(scope, Is.Not.Null);
            Assert.That(item.modifiedState, Is.Not.SameAs(original));
            Assert.That(item.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).valueInt, Is.EqualTo(3));
            Assert.That(item.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).valueInt, Is.EqualTo(3));
            item.modifiedState.GetTag("other").SetInt(99);
            Assert.That(original.GetTag("other").valueInt, Is.EqualTo(7));
            Assert.That(item.state, Is.SameAs(baseState));
            Assert.That(ControllerTooltipBridge.FindSnapshot(item), Is.Not.Null);
        }
        Assert.That(item.modifiedState, Is.SameAs(original));
        Assert.That(ControllerTooltipBridge.FindSnapshot(item), Is.Null);
        Assert.That(GraphUtils.NativeTransfers, Has.Count.EqualTo(transfers));
    }

    [Test]
    public void Begin_NestedItems_RestoresInStackOrder()
    {
        var first = Controller();
        var second = Controller();
        var original = first.modifiedState;
        var secondOriginal = second.modifiedState;
        using var registration = Register(3);
        using (ControllerTooltipBridge.Begin(first))
        {
            var outer = first.modifiedState;
            using (ControllerTooltipBridge.Begin(first))
            using (ControllerTooltipBridge.Begin(second))
            {
                Assert.That(first.modifiedState, Is.Not.SameAs(outer));
                Assert.That(second.modifiedState, Is.Not.SameAs(secondOriginal));
            }
            Assert.That(first.modifiedState, Is.SameAs(outer));
            Assert.That(second.modifiedState, Is.SameAs(secondOriginal));
        }
        Assert.That(first.modifiedState, Is.SameAs(original));
    }

    [Test]
    public void Finalizer_RestoresStateAndReturnsOriginalException()
    {
        var item = new GameItemElement(ControllerItem.ItemId);
        var original = item.modifiedState;
        using var registration = Register(3);
        ControllerTooltipPatch.Prefix(item, out var scope);
        var exception = new InvalidOperationException("native failure");
        Assert.That(ControllerTooltipPatch.Finalizer(exception, scope), Is.SameAs(exception));
        Assert.That(item.modifiedState, Is.SameAs(original));
        Assert.That(ControllerTooltipPatch.Finalizer(null, null), Is.Null);
    }

    [Test]
    public void Dispose_ExceptionAndExternalReplacement_PreservesExceptionAndReplacement()
    {
        var item = Controller();
        var replacement = new TagSystem();
        using var registration = Register(3);
        var error = Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = ControllerTooltipBridge.Begin(item);
            item.modifiedState = replacement;
            throw new InvalidOperationException("failure");
        }));
        Assert.That(error!.Message, Is.EqualTo("failure"));
        Assert.That(item.modifiedState, Is.SameAs(replacement));
        Assert.That(ControllerTooltipBridge.FindSnapshot(item), Is.Null);
    }

    [Test]
    public void Begin_ZeroOverflowUnrelatedAndDisabled_AreNoOps()
    {
        var item = Controller();
        var original = item.modifiedState;
        Assert.That(ControllerTooltipBridge.Begin(item), Is.Null);
        using (Register(0)) Assert.That(ControllerTooltipBridge.Begin(item), Is.Null);
        using (Register(3)) Assert.That(ControllerTooltipBridge.Begin(new GameItem("unrelated")), Is.Null);
        var grid = new GameGridInventory();
        var second = Controller();
        grid.Place(item);
        grid.Place(second);
        grid.Touch(item, second);
        using (Register(int.MaxValue)) Assert.That(ControllerTooltipBridge.Begin(item), Is.Null);
        Assert.That(item.modifiedState, Is.SameAs(original));
    }

    [Test]
    public void Register_ReplacementAndLateScopeDisposal_DoNotDisableNewReader()
    {
        var item = Controller();
        var original = item.modifiedState;
        var older = Register(3);
        var scope = ControllerTooltipBridge.Begin(item);
        using var newer = Register(5);
        older.Dispose();
        scope!.Dispose();
        Assert.That(item.modifiedState, Is.SameAs(original));
        using (ControllerTooltipBridge.Begin(item))
            Assert.That(item.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).valueInt, Is.EqualTo(5));
    }

    [Test]
    public void Begin_ReaderFailure_ReportsAndLeavesStateUnchanged()
    {
        var item = Controller();
        var original = item.modifiedState;
        string? message = null;
        using var registration = ControllerTooltipBridge.Register(new NativeNetworkScanner(new Settings(-1)), text => message = text);
        Assert.That(ControllerTooltipBridge.Begin(item), Is.Null);
        Assert.That(message, Does.Contain("projection failed"));
        Assert.That(item.modifiedState, Is.SameAs(original));
    }

    private static GameItem Controller() => new(ControllerItem.ItemId);

    private static IDisposable Register(int demand) =>
        ControllerTooltipBridge.Register(new NativeNetworkScanner(new Settings(demand)), _ => { });

    private sealed class Settings : IGridPowerSettingsPort
    {
        private readonly int _demand;
        internal Settings(int demand) => _demand = demand;
        public int GetPowerUsage(GridNodeKind kind) => _demand;
    }
}
