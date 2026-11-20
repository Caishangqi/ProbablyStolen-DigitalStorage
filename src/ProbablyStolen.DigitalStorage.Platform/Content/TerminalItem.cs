using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
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

    /// <summary>Universal modules as on vanilla machines, plus modules made for the terminal.</summary>
    private static readonly string[] ModuleTypes = { "MODULE_TYPE_UNIVERSAL", StackingModule.TerminalModuleType };
    private readonly ButtonIcons _icons;
    private readonly TransferDiagnostics _transfers;
    private readonly CompatibilityDiagnostics _compatibility;
    private readonly ModText _text;
    private readonly ItemAppearance _appearance;
    private readonly MachineContentWindow _windows;
    private readonly GridNetworkPowerCoordinator _networkPower;
    private readonly Func<bool> _diagnosticsEnabled;
    private readonly Func<bool> _identityLogEnabled;
    private readonly ModLog _log;
    private readonly NativeNetworkEvents _events;
    private readonly TerminalSearchService _search;
    private readonly ITerminalViewSettings _view;
    private readonly TerminalRegistry _registry;
    private readonly Func<bool> _testViewEnabled;

    internal TerminalItem(ModText text, ItemAppearance appearance, MachineContentWindow windows,
        GridNetworkPowerCoordinator networkPower, Func<bool> diagnosticsEnabled, Func<bool> identityLogEnabled, ModLog log,
        NativeNetworkEvents events,
        TerminalSearchService search, ITerminalViewSettings view, TerminalRegistry registry, Func<bool> testViewEnabled,
        ButtonIcons icons, TransferDiagnostics transfers, CompatibilityDiagnostics compatibility)
    {
        _transfers = transfers;
        _compatibility = compatibility;
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _testViewEnabled = testViewEnabled ?? throw new ArgumentNullException(nameof(testViewEnabled));
        _events = events ?? throw new System.ArgumentNullException(nameof(events));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _text = text;
        _appearance = appearance;
        _windows = windows;
        _networkPower = networkPower ?? throw new System.ArgumentNullException(nameof(networkPower));
        _diagnosticsEnabled = diagnosticsEnabled;
        _identityLogEnabled = identityLogEnabled ?? throw new ArgumentNullException(nameof(identityLogEnabled));
        _log = log;
    }

    private RichText RenderStoredTooltip(GameItemElement root, ItemKey key)
    {
        if (root.parentInventory is not GameGridInventory { overrideLockInsert: true }) return root.GetTooltip();
        return TerminalTooltipScope.Render(root, root.GetTooltip, () =>
        {
            try
            {
                if (_networkPower.Storage.ForgetStoredLore(root, key)) _events.Post(NetworkChangeReason.ContentsChanged);
            }
            catch (Exception error)
            {
                _compatibility.ReportLoreFailure(error.GetType().Name);
            }
        });
    }

    internal GameItem Create()
    {
        _appearance.EnsureReady(SpriteKey);
        var item = ItemDirectory.CreateEmptyItem(ItemId);
        try
        {
            _appearance.Apply(item, SpriteKey, Width, Height);
            _windows.Attach(item, moduleTypes: ModuleTypes);
            var storage = new StorageTerminalServices(_text, _view,
                () => _networkPower.ReadTerminalInventory(item),
                (entry, pieces, operation) => _networkPower.ExtractPiecesToPlayer(item, entry.Key, pieces, operation),
                (batch, target, operation) => _networkPower.ExtractBatch(item, batch, target, operation),
                TerminalFeatures.AttributeCaptures,
                (items, diagnostic) => _networkPower.StoreSelected(item, items, diagnostic),
                (source, amount, diagnostic) => _networkPower.StoreSingle(item, source, amount, diagnostic),
                (sources, amount) => _networkPower.StoreRefusals(item, sources, amount),
                key => _networkPower.FindStoredSample(item, key),
                key => _networkPower.Storage.FindPreviewSample(item, key), RenderStoredTooltip,
                _diagnosticsEnabled, _identityLogEnabled, message => _log.Log(message), _events.Subscribe, _search, _transfers);
            new TerminalWindow(_registry, storage, _view, _testViewEnabled, _icons).Attach(item);
            new TerminalItemDrop(item, (sources, amount) => _networkPower.StoreRefusals(item, sources, amount),
                (source, amount) => _networkPower.StoreSingle(item, source, amount), _text, message => _log.Log(message)).Attach();
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }
}
