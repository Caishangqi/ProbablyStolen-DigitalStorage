using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
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
    private readonly ItemContentSynchronization _contentSynchronization;
    private readonly TerminalSearchService _search;
    private readonly ITerminalViewSettings _view;

    internal TerminalItem(ModText text, ItemAppearance appearance, MachineContentWindow windows,
        GridNetworkPowerCoordinator networkPower, Func<bool> diagnosticsEnabled, ModLog log, NativeNetworkEvents events,
        ItemContentSynchronization contentSynchronization, TerminalSearchService search, ITerminalViewSettings view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _events = events ?? throw new System.ArgumentNullException(nameof(events));
        _contentSynchronization = contentSynchronization ?? throw new ArgumentNullException(nameof(contentSynchronization));
        _search = search ?? throw new ArgumentNullException(nameof(search));
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
            _appearance.Apply(item, SpriteKey, Width, Height);
            NativeItemContentInitialization.Initialize(item, _contentSynchronization);
            _windows.Attach(item);
            var storageWindow = new TerminalStorageWindow(_text,
                () => _networkPower.ReadTerminalInventory(item),
                (entry, amount) => _networkPower.ExtractToPlayer(item, entry.Key, amount),
                key => _networkPower.StackLimit(item, key),
                () => TerminalOpenRouter.OpenNative(item),
                (items, diagnostic) => _networkPower.StoreSelected(item, items, diagnostic),
                (source, amount, diagnostic) => _networkPower.StoreSingle(item, source, amount, diagnostic),
                (source, amount) => _networkPower.CanStore(item, source, amount),
                key => _networkPower.FindStoredSample(item, key),
                _diagnosticsEnabled, message => _log.Log(message), _events.Subscribe, _search, _view);
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
}
