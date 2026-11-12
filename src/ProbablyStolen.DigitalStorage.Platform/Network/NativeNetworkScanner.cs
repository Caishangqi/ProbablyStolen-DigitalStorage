using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Content;

namespace ProbablyStolen.DigitalStorage.Platform.Network;

internal sealed class NativeNetworkScanner
{
    private readonly IGridPowerSettingsPort _powerSettings;
    private readonly Action<string>? _reportError;
    private readonly Func<GameItem, IReadOnlyList<IStorageCell>>? _storageCellResolver;
    private readonly GridPropagator _propagator = new();

    internal NativeNetworkScanner(IGridPowerSettingsPort powerSettings,
        Action<string>? reportError = null,
        Func<GameItem, IReadOnlyList<IStorageCell>>? storageCellResolver = null)
    {
        _powerSettings = powerSettings ?? throw new ArgumentNullException(nameof(powerSettings));
        _reportError = reportError;
        _storageCellResolver = storageCellResolver;
    }

    internal NativeGridSnapshot Capture(GameItem focus)
    {
        if (focus == null)
        {
            throw new ArgumentNullException(nameof(focus));
        }

        var machines = FindMachinesInCurrentGrid(focus);
        var nodes = new GridNode[machines.Count];
        for (var index = 0; index < machines.Count; index++)
        {
            var machine = machines[index];
            var kind = GetKind(machine);
            var storageCells = kind == GridNodeKind.Drive
                ? _storageCellResolver?.Invoke(machine) ?? Array.Empty<IStorageCell>()
                : Array.Empty<IStorageCell>();
            var occupied = kind == GridNodeKind.Drive ? CountOccupiedDriveSlots(machine) : 0;
            nodes[index] = new GridNode(index, kind, _powerSettings.GetPowerUsage(kind), occupied,
                kind == GridNodeKind.Drive ? 14 : 0, storageCells);
        }

        var edges = new List<GridConnection>();
        for (var index = 0; index < machines.Count; index++)
        {
            if (machines[index].parentInventory is not GameGridInventory)
            {
                continue;
            }

            foreach (var adjacent in machines[index].FindAdjacentType<GameItem>())
            {
                var adjacentIndex = FindByReference(machines, adjacent);
                if (adjacentIndex > index)
                {
                    edges.Add(new GridConnection(index, adjacentIndex));
                }
            }
        }

        return new NativeGridSnapshot(machines, _propagator.Propagate(nodes, edges));
    }

    private static List<GameItem> FindMachinesInCurrentGrid(GameItem focus)
    {
        var parentInventory = focus.parentInventory;
        if (parentInventory is not GameGridInventory)
        {
            return new List<GameItem> { focus };
        }

        var result = new List<GameItem>();
        foreach (var item in parentInventory.childItems)
        {
            if (IsNetworkMachine(item) && ReferenceEquals(item.parentInventory, parentInventory) &&
                FindByReference(result, item) < 0)
            {
                result.Add(item);
            }
        }

        if (FindByReference(result, focus) < 0)
        {
            result.Add(focus);
        }

        return result;
    }

    /// <summary>Gets whether an item is a terminal, drive or controller.</summary>
    internal static bool IsNetworkMachine(GameItem item) =>
        item.identifier == ControllerItem.ItemId ||
        item.identifier == DriveItem.ItemId ||
        item.identifier == TerminalItem.ItemId;

    private static GridNodeKind GetKind(GameItem item)
    {
        if (item.identifier == ControllerItem.ItemId)
        {
            return GridNodeKind.Controller;
        }

        if (item.identifier == DriveItem.ItemId)
        {
            return GridNodeKind.Drive;
        }

        if (item.identifier == TerminalItem.ItemId)
        {
            return GridNodeKind.Terminal;
        }

        throw new ArgumentException("The item is not a DigitalStorage network machine.", nameof(item));
    }

    private static int CountOccupiedDriveSlots(GameItem drive)
    {
        return GraphUtils.FindAllChildrenType<GameSlotInventory>(drive).Count(slot =>
            slot.identifier != null && slot.identifier.StartsWith(CellContentWindow.BayPrefix, StringComparison.Ordinal) &&
            slot.childItem != null);
    }

    private static int FindByReference(IReadOnlyList<GameItem> items, GameItem target)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], target))
            {
                return index;
            }
        }

        return -1;
    }
}
