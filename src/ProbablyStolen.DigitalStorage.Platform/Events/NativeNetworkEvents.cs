using System;
using ProbablyStolen.DigitalStorage.Common.Application.Events;

namespace ProbablyStolen.DigitalStorage.Platform.Events;

/// <summary>
/// Turns native inventory callbacks into <see cref="NetworkChanged"/> signals. Handlers only post a reason and never
/// scan: an item's <c>onSlotRemoveItemFunc</c> runs before the item leaves its inventory (,
/// <c>GameGridInventory.Expel</c>), so the topology is read on the next frame instead.
/// </summary>
internal sealed class NativeNetworkEvents
{
    private readonly StorageEventBus _bus;

    internal NativeNetworkEvents(StorageEventBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    /// <summary>Exposes subscription only, so views cannot post network signals.</summary>
    internal IDisposable Subscribe(Action<NetworkChanged> handler) => _bus.Subscribe(handler);

    /// <summary>Signals a machine entering or leaving any inventory, including moves inside one grid.</summary>
    internal void AttachMachine(GameItem machine)
    {
        machine.onSlotAddItemFunc += (_, _, _) => Post(NetworkChangeReason.MachineAdded);
        machine.onSlotRemoveItemFunc += (_, _, _) => Post(NetworkChangeReason.MachineRemoved);
    }

    /// <summary>Signals after a slot's contents changed; the End callbacks run once the parent link is updated.</summary>
    internal void AttachSlot(GameInventory slot, NetworkChangeReason added, NetworkChangeReason removed)
    {
        slot.onSlotAddItemEndFunc += (_, _, _) => Post(added);
        slot.onSlotRemoveItemEndFunc += (_, _, _) => Post(removed);
    }

    /// <summary>Posts unless a save is decoding; the game closes terminals once loading completes.</summary>
    internal void Post(NetworkChangeReason reason)
    {
        if (SaveManager.IsDecoding) return;
        _bus.Post(new NetworkChanged(reason));
    }
}
