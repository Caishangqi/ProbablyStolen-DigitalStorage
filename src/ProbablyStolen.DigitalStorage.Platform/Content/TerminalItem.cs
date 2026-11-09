using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class TerminalItem
{
    internal const string ItemId = "digital_storage.storage_terminal";
    internal const string SpriteKey = "terminal";
    internal const int Width = 3;
    internal const int Height = 3;
    internal const string NameKey = "terminal_name";
    internal const string DescriptionKey = "terminal_desc";
    private readonly ModText _text;
    private readonly ItemAppearance _appearance;
    private readonly MachineContentWindow _windows;
    private readonly GridNetworkPowerCoordinator _networkPower;
    private readonly Func<bool> _diagnosticsEnabled;
    private readonly ModLog _log;
    private readonly NativeNetworkEvents _events;

    internal TerminalItem(ModText text, ItemAppearance appearance, MachineContentWindow windows,
        GridNetworkPowerCoordinator networkPower, Func<bool> diagnosticsEnabled, ModLog log, NativeNetworkEvents events)
    {
        _events = events ?? throw new System.ArgumentNullException(nameof(events));
        _text = text;
        _appearance = appearance;
        _windows = windows;
        _networkPower = networkPower ?? throw new System.ArgumentNullException(nameof(networkPower));
        _diagnosticsEnabled = diagnosticsEnabled;
        _log = log;
    }

    internal GameItem Create()
    {
        _appearance.EnsureReady(SpriteKey);
        var item = ItemDirectory.CreateEmptyItem(ItemId);
        try
        {
            item.name = _text.Get(NameKey);
            item.shortDescription = _text.Get(DescriptionKey);
            _appearance.Apply(item, SpriteKey, Width, Height);
            item.unitValue = ItemPrices.Terminal;
            _windows.Attach(item);
            var storageWindow = new TerminalStorageWindow(_text,
                () => _networkPower.ReadTerminalInventory(item),
                (entry, amount) => _networkPower.ExtractToPlayer(item, entry.Key, amount),
                key => _networkPower.StackLimit(item, key),
                () => TerminalOpenRouter.OpenNative(item),
                () => OpenPartitions(item),
                (items, diagnostic) => _networkPower.StoreSelected(item, items, diagnostic),
                (source, amount, diagnostic) => _networkPower.StoreSingle(item, source, amount, diagnostic),
                (source, amount) => _networkPower.CanStore(item, source, amount),
                key => _networkPower.FindStoredSample(item, key),
                _diagnosticsEnabled, message => _log.Log(message), _events.Subscribe);
            storageWindow.Attach(item);
            new TerminalItemDrop(item, (source, amount) => _networkPower.CanStore(item, source, amount),
                (source, amount) => _networkPower.StoreSingle(item, source, amount), message => _log.Log(message)).Attach();
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }

    /// <summary>Opens partition editing for every cell on the terminal's grid; returns false when there is none.</summary>
    private bool OpenPartitions(GameItem terminal)
    {
        IReadOnlyList<CellBayReference> bays;
        try
        {
            bays = _networkPower.ListCellBays(terminal);
        }
        catch (Exception exception)
        {
            _log.Log($"Digital Storage: terminal partition scan failed error={exception.GetType().Name}");
            return false;
        }

        if (bays.Count == 0) return false;
        var labels = bays.Select(bay => _text.Get("terminal_partition_label", bay.Drive, bay.Slot)).ToArray();
        new PartitionConfigurationWindow(_text, labels, index => new CellPartitionSource(bays[index].Bay),
            DriveItem.CaptureDraggedIdentity).Open();
        return true;
    }
}
