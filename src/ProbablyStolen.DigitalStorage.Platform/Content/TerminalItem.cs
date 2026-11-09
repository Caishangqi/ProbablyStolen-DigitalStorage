using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;

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
            _windows.Attach(item);
            var storageWindow = new TerminalStorageWindow(_text,
                () => _networkPower.ReadTerminalInventory(item),
                (entry, amount) => _networkPower.ExtractToPlayer(item, entry.Key, amount),
                () => _networkPower.StoreFromPlayer(item),
                (items, diagnostic) => _networkPower.StoreSelected(item, items, diagnostic),
                (source, amount, diagnostic) => _networkPower.StoreSingle(item, source, amount, diagnostic),
                (source, amount) => _networkPower.CanStore(item, source, amount),
                key => _networkPower.FindStoredSample(item, key),
                _diagnosticsEnabled, message => _log.Log(message), _events.Subscribe);
            storageWindow.Attach(item);
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }
}
