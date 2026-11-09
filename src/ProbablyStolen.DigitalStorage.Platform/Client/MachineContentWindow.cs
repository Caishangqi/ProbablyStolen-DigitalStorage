using System;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class MachineContentWindow
{
    private readonly ModText _text;
    private readonly GridNetworkPowerCoordinator _networkPower;
    private readonly NativeNetworkEvents _events;

    internal MachineContentWindow(ModText text, GridNetworkPowerCoordinator networkPower, NativeNetworkEvents events)
    {
        _text = text;
        _networkPower = networkPower;
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    internal void Attach(GameItem machine, GridPixelElement? cellBays = null,
        bool acceptsBattery = false, bool consumesNetworkPower = false, Action? configurePartitions = null)
    {
        var battery = new GameSlotInventory();
        var modules = new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG,
            MachineHelper.DEFAULT_MODULE_BAY_WIDTH);
        var panels = cellBays == null
            ? new[]
            {
                MachineHelper.PowerPanel(battery),
                MachineHelper.Panel(_text.Get("modules_panel"), modules, "module")
            }
            : new[]
            {
                MachineHelper.PowerPanel(battery),
                MachineHelper.Panel(_text.Get("modules_panel"), modules, "module"),
                MachineHelper.Panel(_text.Get("cells_panel"), cellBays, "cells")
            };
        machine.SetContentWindow(MachineHelper.CreateMachineryWindow(true, panels));
        if (configurePartitions != null)
        {
            machine.contentWindow.AddHeaderButton(new MachineryButton(_text.Get("partition_configure"), configurePartitions));
        }
        MachineryHelper.InitMachinery(machine);
        machine.EnableTag(MachineHelper.STANDARD_MACHINE_TAG);
        if (acceptsBattery)
        {
            MachineHelper.SetupBatterySlot(battery, machine);
            _events.AttachSlot(battery, NetworkChangeReason.BatteryChanged, NetworkChangeReason.BatteryChanged);
        }
        else
        {
            // Keep storage node zero stable for existing saves and allow any legacy battery to be removed.
            battery.mayInventoryAddItemFunc = (_, _) => false;
            battery.maxSlotAddItemFunc = (_, _, _) => 0;
            battery.maxSlotAmountItemFunc = (_, _, _) => 0;
        }

        MachineHelper.SetupModuleBay(modules, machine);
        machine.mayStackItemFunc = (_, _) => false;
        machine.maxSlotAmountItemFunc = (_, _, _) => 1;
        _events.AttachMachine(machine);
        if (consumesNetworkPower)
        {
            machine.onCycleEndLateSlotItemFunc += (_, _, _) => _networkPower.OnControllerCycleEnd(machine);
            // Multicast delegates run in subscription order, so the signal follows this turn's power draw.
            machine.onCycleEndLateSlotItemFunc += (_, _, _) => _events.Post(NetworkChangeReason.PowerCycle);
        }

        var lifecycle = new MachineContentLifecycle(new NativeMachineContents(machine));
        machine.contentWindow.openCallback += () => lifecycle.RestoreIfReady(SaveManager.IsDecoding);
        machine.onOneSecondItemFunc += (_, _, _) => lifecycle.RestoreIfReady(SaveManager.IsDecoding);
    }

    private sealed class NativeMachineContents : IMachineContentsPort
    {
        private readonly GameItem _machine;

        internal NativeMachineContents(GameItem machine)
        {
            _machine = machine;
        }

        public void RestoreModules()
        {
            MachineHelper.LoadUpgrade(_machine);
            MachineHelper.OnLoad(_machine);
        }
    }
}
