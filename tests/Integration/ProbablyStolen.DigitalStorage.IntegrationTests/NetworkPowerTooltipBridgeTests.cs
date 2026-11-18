using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Mod.Patches;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
[NonParallelizable]
public sealed class NetworkPowerTooltipBridgeTests
{
    [Test]
    public void Begin_ControllerProjectsNetworkDemandAndRestoresWithoutChangingBaseState()
    {
        var item = new GameItem(ControllerItem.ItemId);
        var original = item.modifiedState;
        var baseState = item.state;
        original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).Enable().SetInt(1);
        original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).Enable().SetInt(2);
        using var registration = Register(3);
        using (var scope = NetworkPowerTooltipBridge.Begin(item))
        {
            Assert.That(scope, Is.Not.Null);
            Assert.That(item.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).valueInt, Is.EqualTo(3));
            Assert.That(item.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).valueInt, Is.EqualTo(3));
            Assert.That(item.state, Is.SameAs(baseState));
        }
        Assert.That(item.modifiedState, Is.SameAs(original));
        Assert.That(original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).valueInt, Is.EqualTo(1));
    }

    [TestCase(DriveItem.ItemId)]
    [TestCase(TerminalItem.ItemId)]
    public void Begin_DeviceSuppressesNativeDrawAndRestoresWithoutChangingBaseState(string itemId)
    {
        var item = new GameItem(itemId);
        var original = item.modifiedState;
        var baseState = item.state;
        original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).Enable().SetInt(1);
        original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).Enable().SetInt(2);
        original.GetTag("other").SetInt(7);
        var transfers = GraphUtils.NativeTransfers.Count;
        using var registration = Register(3);
        using (var scope = NetworkPowerTooltipBridge.Begin(item))
        {
            Assert.That(scope, Is.Not.Null);
            Assert.That(item.modifiedState, Is.Not.SameAs(original));
            Assert.That(item.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).valueInt, Is.Zero);
            item.modifiedState.GetTag("other").SetInt(99);
            Assert.That(original.GetTag("other").valueInt, Is.EqualTo(7));
            Assert.That(item.state, Is.SameAs(baseState));
        }
        Assert.That(item.modifiedState, Is.SameAs(original));
        Assert.That(original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).valueInt, Is.EqualTo(1));
        Assert.That(GraphUtils.NativeTransfers, Has.Count.EqualTo(transfers));
    }

    [Test]
    public void Begin_ZeroControllerDemandSuppressesStaleNativeDrawAndRestoresIt()
    {
        var item = new GameItem(ControllerItem.ItemId);
        var original = item.modifiedState;
        original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).Enable().SetInt(1);
        original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).Enable().SetInt(1);
        using var registration = Register(0);
        using (var scope = NetworkPowerTooltipBridge.Begin(item))
        {
            Assert.That(scope, Is.Not.Null);
            Assert.That(item.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).valueInt, Is.Zero);
            Assert.That(item.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).valueInt, Is.Zero);
        }
        Assert.That(item.modifiedState, Is.SameAs(original));
        Assert.That(original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).valueInt, Is.EqualTo(1));
        Assert.That(original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).valueInt, Is.EqualTo(1));
    }

    [Test]
    public void Begin_NestedItems_RestoresInStackOrder()
    {
        var first = new GameItem(ControllerItem.ItemId);
        var second = new GameItem(DriveItem.ItemId);
        var firstOriginal = first.modifiedState;
        var secondOriginal = second.modifiedState;
        using var registration = Register(3);
        using (NetworkPowerTooltipBridge.Begin(first))
        {
            var outer = first.modifiedState;
            using (NetworkPowerTooltipBridge.Begin(first))
            using (NetworkPowerTooltipBridge.Begin(second))
            {
                Assert.That(first.modifiedState, Is.Not.SameAs(outer));
                Assert.That(second.modifiedState, Is.Not.SameAs(secondOriginal));
            }
            Assert.That(first.modifiedState, Is.SameAs(outer));
            Assert.That(second.modifiedState, Is.SameAs(secondOriginal));
        }
        Assert.That(first.modifiedState, Is.SameAs(firstOriginal));
    }

    [Test]
    public void Finalizer_RestoresStateAndReturnsOriginalException()
    {
        var item = new GameItemElement(TerminalItem.ItemId);
        var original = item.modifiedState;
        original.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).Enable().SetInt(1);
        using var registration = Register(3);
        NetworkPowerTooltipPatch.Prefix(item, out var scope);
        Assert.That(scope, Is.Not.Null);
        var builder = new RichTextBuilder();
        MachineryHelper.CreateMachineryTooltip(builder, item);
        Assert.That(builder.Lines, Is.Empty);
        var exception = new InvalidOperationException("native failure");
        Assert.That(NetworkPowerTooltipPatch.Finalizer(exception, scope), Is.SameAs(exception));
        Assert.That(item.modifiedState, Is.SameAs(original));
        Assert.That(NetworkPowerTooltipPatch.Finalizer(null, null), Is.Null);
        MachineryHelper.CreateMachineryTooltip(builder, item);
        Assert.That(builder.Lines, Is.EqualTo(new[] { "native_power" }));
    }

    [Test]
    public void Dispose_ExternalReplacement_PreservesReplacement()
    {
        var item = new GameItem(ControllerItem.ItemId);
        var replacement = new TagSystem();
        using var registration = Register(3);
        using (NetworkPowerTooltipBridge.Begin(item)) item.modifiedState = replacement;
        Assert.That(item.modifiedState, Is.SameAs(replacement));
    }

    [Test]
    public void Begin_UnrelatedAndDisabled_AreNoOps()
    {
        using var registration = Register(3);
        var unrelated = new GameItem("unrelated");
        var original = unrelated.modifiedState;
        Assert.That(NetworkPowerTooltipBridge.Begin(unrelated), Is.Null);
        Assert.That(unrelated.modifiedState, Is.SameAs(original));
    }

    [Test]
    public void Register_ReplacementAndLateScopeDisposal_DoNotDisableNewRegistration()
    {
        var item = new GameItem(DriveItem.ItemId);
        var original = item.modifiedState;
        using var older = Register(3);
        var scope = NetworkPowerTooltipBridge.Begin(item);
        using var newer = Register(3);
        older.Dispose();
        scope!.Dispose();
        Assert.That(item.modifiedState, Is.SameAs(original));
        using var next = NetworkPowerTooltipBridge.Begin(item);
        Assert.That(next, Is.Not.Null);
    }

    private static IDisposable Register(int powerUsage) => NetworkPowerTooltipBridge.Register(
        new NativeNetworkScanner(new PowerSettings(powerUsage)), _ => { });

    private sealed class PowerSettings : ProbablyStolen.DigitalStorage.Common.Ports.IGridPowerSettingsPort
    {
        private readonly int _powerUsage;
        internal PowerSettings(int powerUsage) => _powerUsage = powerUsage;
        public int GetPowerUsage(ProbablyStolen.DigitalStorage.Common.Domain.Network.GridNodeKind kind) => _powerUsage;
    }
}
