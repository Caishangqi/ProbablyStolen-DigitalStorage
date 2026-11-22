using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Mod.Patches;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Composition;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
[NonParallelizable]
public sealed class NativeTooltipDispatcherTests
{
    [SetUp]
    public void ResetHookFailure() => ModHook.ThrowAfterTooltipSubscription = false;

    [TearDown]
    public void ResetCompositionState()
    {
        ModHook.Reset();
        ModHook.ThrowAfterTooltipSubscription = false;
    }

    [Test]
    public void Composition_RegistersAndDispatchesCompleteNetworkOutputInOrder()
    {
        var settings = new Settings();
        var power = new GridNetworkPowerCoordinator();
        var errors = new List<string>();
        using var composition = Create(settings, power, errors);
        composition.Enable();
        composition.Enable();
        Assert.That(ModHook.TooltipSubscriberCount, Is.EqualTo(1));

        var item = new GameItem(ControllerItem.ItemId) { ReceivingModuleBonus = true };
        var builder = new RichTextBuilder();
        ModHook.RaiseTooltip(builder, item);
        Assert.That(builder.Lines, Is.EqualTo(new[]
        {
            "network_tooltip_state", "network_tooltip_total_power:3", " mech_container_tooltip"
        }));
        Assert.That(settings.Calls, Is.EqualTo(1));
        Assert.That(power.Calls, Is.EqualTo(1));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Composition_SelectsCellModuleNetworkAndUnrelatedWithoutUnneededScans()
    {
        var settings = new Settings();
        using var composition = Create(settings, new GridNetworkPowerCoordinator(), new List<string>());
        composition.Enable();
        Assert.That(ModHook.TooltipSubscriberCount, Is.EqualTo(1));

        var cell = new RichTextBuilder();
        ModHook.RaiseTooltip(cell, new GameItem(StorageCellTier.All[0].CellId));
        Assert.That(cell.Lines, Is.EqualTo(new[] { "cell" }), string.Join("|", cell.Lines));

        var component = new RichTextBuilder();
        ModHook.RaiseTooltip(component, new GameItem(StorageCellTier.All[0].ComponentId));
        Assert.That(component.Lines, Is.Empty);

        var module = new RichTextBuilder();
        ModHook.RaiseTooltip(module, new GameItem(CellUpgradeInventory.TypeLimitId));
        Assert.That(module.Lines, Is.Empty);

        var unrelated = new RichTextBuilder();
        ModHook.RaiseTooltip(unrelated, new GameItem("unrelated"));
        Assert.That(unrelated.Lines, Is.Empty);
        Assert.That(settings.Calls, Is.Zero);
    }

    [Test]
    public void Composition_CapturesFreshlyOncePerControllerTooltip()
    {
        var settings = new Settings();
        using var composition = Create(settings, new GridNetworkPowerCoordinator(), new List<string>());
        composition.Enable();
        Assert.That(ModHook.TooltipSubscriberCount, Is.EqualTo(1));
        var item = new GameItemElement(ControllerItem.ItemId);
        var grid = new GameGridInventory();
        grid.Place(item);
        var builder = new RichTextBuilder();
        NetworkPowerTooltipPatch.Prefix(item, out var scope);
        try
        {
            ModHook.RaiseTooltip(builder, item);
        }
        finally
        {
            NetworkPowerTooltipPatch.Finalizer(null, scope);
        }
        Assert.That(settings.Calls, Is.EqualTo(1));
        Assert.That(builder.Lines, Is.EqualTo(new[] { "network_tooltip_state", "network_tooltip_total_power:3", " mech_container_tooltip" }));

        NetworkPowerTooltipPatch.Prefix(item, out scope);
        try
        {
            ModHook.RaiseTooltip(new RichTextBuilder(), item);
        }
        finally
        {
            NetworkPowerTooltipPatch.Finalizer(null, scope);
        }
        Assert.That(settings.Calls, Is.EqualTo(2));
    }

    [Test]
    public void Composition_RendersAllCellLinesWhileActive()
    {
        var cells = new BasicCellHandler(new ModText()) { TooltipLineCount = 3 };
        using var composition = Create(new Settings(), new GridNetworkPowerCoordinator(), new List<string>(), cells);
        composition.Enable();
        var builder = new RichTextBuilder();
        ModHook.RaiseTooltip(builder, new GameItem(StorageCellTier.All[0].CellId));
        Assert.That(builder.Lines, Is.EqualTo(new[] { "cell", "cell_2", "cell_3" }));
    }

    [Test]
    public void Composition_DisposalDuringContributionStopsRetainedCallbackAndFollowingHandlers()
    {
        var cells = new BasicCellHandler(new ModText()) { TooltipLineCount = 3 };
        var composition = Create(new Settings(), new GridNetworkPowerCoordinator(), new List<string>(), cells);
        composition.Enable();
        var builder = new RichTextBuilder { OnAdd = _ => composition.Dispose() };
        ModHook.RaiseTooltip(builder, new GameItem(StorageCellTier.All[0].CellId));
        Assert.That(builder.Lines, Is.EqualTo(new[] { "cell" }));
        Assert.That(ModHook.TooltipSubscriberCount, Is.Zero);
        ModHook.RaiseTooltip(new RichTextBuilder(), new GameItem(ControllerItem.ItemId));
        Assert.That(NetworkPowerTooltipBridge.Begin(new GameItem(ControllerItem.ItemId)), Is.Null);
        Assert.That(TypeLimitModuleTooltipBridge.Begin(new GameItem(CellUpgradeInventory.TypeLimitId)), Is.Null);
        composition.Dispose();
    }

    [Test]
    public void Composition_DisposalDuringNetworkStatusSkipsDetailsAndPostlude()
    {
        var composition = Create(new Settings(), new GridNetworkPowerCoordinator(), new List<string>());
        composition.Enable();
        var builder = new RichTextBuilder { OnAdd = _ => composition.Dispose() };
        ModHook.RaiseTooltip(builder, new GameItem(ControllerItem.ItemId));
        Assert.That(builder.Lines, Is.EqualTo(new[] { "network_tooltip_state" }));
        Assert.That(ModHook.TooltipSubscriberCount, Is.Zero);
    }

    [Test]
    public void Composition_CapturesConnectedNetworkOnceForSeveralMachineContributors()
    {
        var settings = new Settings();
        var power = new GridNetworkPowerCoordinator();
        using var composition = Create(settings, power, new List<string>());
        composition.Enable();
        var grid = new GameGridInventory();
        var controller = new GameItem(ControllerItem.ItemId);
        var drive = new GameItem(DriveItem.ItemId);
        var terminal = new GameItem(TerminalItem.ItemId);
        grid.Place(controller);
        grid.Place(drive);
        grid.Place(terminal);
        grid.Touch(controller, drive);
        grid.Touch(drive, terminal);

        var builder = new RichTextBuilder();
        ModHook.RaiseTooltip(builder, drive);
        Assert.That(settings.Calls, Is.EqualTo(3));
        Assert.That(power.Calls, Is.EqualTo(1));
        Assert.That(builder.Lines, Is.EqualTo(new[]
        {
            "network_tooltip_state", "network_tooltip_device_power:2", "network_tooltip_drive_slots", " mech_container_tooltip"
        }));

        terminal.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).Enable().SetInt(19);
        builder = new RichTextBuilder();
        ModHook.RaiseTooltip(builder, terminal);
        Assert.That(builder.Lines, Is.EqualTo(new[] { "network_tooltip_state", "network_tooltip_device_power:1", " mech_container_tooltip" }));

        controller.modifiedState.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).Enable().SetInt(19);
        builder = new RichTextBuilder();
        ModHook.RaiseTooltip(builder, controller);
        Assert.That(builder.Lines, Is.EqualTo(new[]
        {
            "network_tooltip_state", "network_tooltip_total_power:6", " mech_container_tooltip"
        }));
    }

    [Test]
    public void Composition_DisposalDuringDetailStopsRemainingDetailRowsAndPostlude()
    {
        var composition = Create(new Settings(), new GridNetworkPowerCoordinator(), new List<string>());
        composition.Enable();
        var builder = new RichTextBuilder
        {
            OnAdd = value => { if (value == "network_tooltip_total_power:3") composition.Dispose(); }
        };
        ModHook.RaiseTooltip(builder, new GameItem(ControllerItem.ItemId));
        Assert.That(builder.Lines, Is.EqualTo(new[] { "network_tooltip_state", "network_tooltip_total_power:3" }));
        Assert.That(ModHook.TooltipSubscriberCount, Is.Zero);
    }

    [Test]
    public void Composition_DisposalDuringScanPreventsNetworkOutput()
    {
        var settings = new Settings();
        using var composition = Create(settings, new GridNetworkPowerCoordinator(), new List<string>());
        settings.OnRead = composition.Dispose;
        composition.Enable();
        var builder = new RichTextBuilder();
        ModHook.RaiseTooltip(builder, new GameItem(ControllerItem.ItemId));
        Assert.That(builder.Lines, Is.Empty);
        Assert.That(ModHook.TooltipSubscriberCount, Is.Zero);
    }

    [Test]
    public void Composition_RetainedCallbackCannotRenderAfterDispose()
    {
        var composition = Create(new Settings(), new GridNetworkPowerCoordinator(), new List<string>());
        composition.Enable();
        var callback = ModHook.TooltipCallback!;
        composition.Dispose();
        var builder = new RichTextBuilder();
        callback(builder, new GameItem(ControllerItem.ItemId));
        Assert.That(builder.Lines, Is.Empty);
    }

    [Test]
    public void Composition_FailedSubscriptionRollsBackProjectionAndHook()
    {
        var composition = Create(new Settings(), new GridNetworkPowerCoordinator(), new List<string>());
        ModHook.ThrowAfterTooltipSubscription = true;
        Assert.Throws<InvalidOperationException>((Action)composition.Enable);
        Assert.That(ModHook.TooltipSubscriberCount, Is.Zero);
        Assert.That(NetworkPowerTooltipBridge.Begin(new GameItem(ControllerItem.ItemId)), Is.Null);
        Assert.That(TypeLimitModuleTooltipBridge.Begin(new GameItem(CellUpgradeInventory.TypeLimitId)), Is.Null);
        Assert.Throws<ObjectDisposedException>((Action)composition.Enable);
        composition.Dispose();
    }

    [Test]
    public void Composition_ReportsFailuresOncePerRegisteredIdentity()
    {
        var errors = new List<string>();
        using var composition = Create(new Settings(), new GridNetworkPowerCoordinator(), errors);
        composition.Enable();
        var cell = new GameItem(StorageCellTier.All[0].CellId) { ThrowTooltip = true };
        ModHook.RaiseTooltip(new RichTextBuilder(), cell);
        ModHook.RaiseTooltip(new RichTextBuilder(), cell);

        var item = new GameItem(ControllerItem.ItemId);
        var builder = new RichTextBuilder { ThrowOn = "network_tooltip_total_power:3" };
        ModHook.RaiseTooltip(builder, item);
        builder = new RichTextBuilder { ThrowOn = "network_tooltip_total_power:3" };
        ModHook.RaiseTooltip(builder, item);

        Assert.That(errors, Has.Count.EqualTo(2), string.Join("|", errors));
        Assert.That(errors[0], Does.Contain("digital_storage:cell"));
        Assert.That(errors[1], Does.Contain("digital_storage:controller"));
    }

    [Test]
    public void Dispatcher_StopsBetweenContributorsWhenDisabledReentrantly()
    {
        var calls = 0;
        NativeTooltipDispatcher? dispatcher = null;
        var contributors = new INativeTooltipContributor[]
        {
            new TestContributor("first", _ => dispatcher!.Disable()),
            new TestContributor("second", _ => calls++)
        };
        dispatcher = new NativeTooltipDispatcher(new NativeNetworkScanner(new Settings()), contributors,
            (_, _) => Assert.Fail("unexpected failure"));
        dispatcher.OnCreateTooltipLate(new RichTextBuilder(), new GameItem("unrelated"));
        Assert.That(calls, Is.Zero);
    }

    private static TooltipComposition Create(Settings settings, GridNetworkPowerCoordinator power, List<string> errors,
        BasicCellHandler? cells = null) =>
        new(new NativeNetworkScanner(settings), power, new ModText(), cells ?? new BasicCellHandler(new ModText()),
            _ => null, errors.Add);

    private sealed class TestContributor : INativeTooltipContributor
    {
        private readonly Action<NativeTooltipContext> _append;
        internal TestContributor(string path, Action<NativeTooltipContext> append)
        {
            Identity = new Identifier("test", path);
            _append = append;
        }
        public Identifier Identity { get; }
        public void Append(NativeTooltipContext context) => _append(context);
    }

    private sealed class Settings : IGridPowerSettingsPort
    {
        internal int Calls;
        internal Action? OnRead;
        public int GetPowerUsage(GridNodeKind kind)
        {
            Calls++;
            OnRead?.Invoke();
            return kind switch
            {
                GridNodeKind.Terminal => 1,
                GridNodeKind.Drive => 2,
                _ => 3
            };
        }
    }
}
