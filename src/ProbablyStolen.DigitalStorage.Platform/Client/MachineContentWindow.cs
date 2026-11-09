using System;
using System.Collections.Generic;
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
        bool acceptsBattery = false, bool consumesNetworkPower = false, bool drivePriority = false)
    {
        var battery = new GameSlotInventory();
        var modules = new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG,
            MachineHelper.DEFAULT_MODULE_BAY_WIDTH);
        var power = MachineHelper.PowerPanel(battery);
        var priority = drivePriority ? new DrivePriorityWindow(machine) : null;
        var panels = new List<MachineryPanel>
        {
            power,
            MachineHelper.Panel(_text.Get("modules_panel"), modules, "module")
        };
        if (cellBays != null)
        {
            panels.Add(MachineHelper.Panel(_text.Get("cells_panel"), cellBays, "cells"));
        }
        if (priority != null)
        {
            panels.Add(MachineHelper.Panel(_text.Get("priority_configure"), priority.Content, "priority"));
        }
        machine.SetContentWindow(MachineHelper.CreateMachineryWindow(true, panels.ToArray()));
        if (priority != null) machine.contentWindow.openCallback += priority.Refresh;
        MachineryHelper.InitMachinery(machine);
        machine.EnableTag(MachineHelper.STANDARD_MACHINE_TAG);
        // Classified like vanilla machines: no nesting in containers, counted as machinery when bought.
        machine.SetGameItemType("MACHINE");
        if (acceptsBattery)
        {
            MachineHelper.SetupBatterySlot(battery, machine);
            _events.AttachSlot(battery, NetworkChangeReason.BatteryChanged, NetworkChangeReason.BatteryChanged);
        }
        else
        {
            // Saves restore child items by breadth-first inventory position and MachineHelper reads the battery at
            // position 0 and modules at 1 (Steam build 26496118, SaveManager.EncodeNode/MachineHelper.GetModuleInv),
            // so the slot stays and is only hidden. A legacy battery keeps it visible until the player removes it.
            battery.mayInventoryAddItemFunc = (_, _) => false;
            battery.maxSlotAddItemFunc = (_, _, _) => 0;
            battery.maxSlotAmountItemFunc = (_, _, _) => 0;
            void SyncPowerPanel() => power.SetHidden(battery.childItem == null);
            machine.contentWindow.openCallback += SyncPowerPanel;
            battery.onSlotRemoveItemEndFunc += (_, _, _) => SyncPowerPanel();
            SyncPowerPanel();
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

        // Loading replaces the name with the saved string (SaveManager.decompiled.cs:165), so a save from an older
        // name keeps showing it. A player's label lives in CUSTOM_NAME_TAG and still wins in GetDisplayName.
        // Writing the localized name back with the next save is a user-approved update of the saved name field.
        var localizedName = machine.name;
        void RestoreName()
        {
            if (!SaveManager.IsDecoding && machine.name != localizedName) machine.name = localizedName;
        }

        machine.contentWindow.openCallback += () =>
        {
            RestoreName();
            machine.contentWindow.SetTitle(machine.GetDisplayName(preferCustomName: true));
            MachineryTitleFit.Fit(machine.contentWindow, Array.Empty<int>());
        };
        machine.onOneSecondItemFunc += (_, _, _) => RestoreName();
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
