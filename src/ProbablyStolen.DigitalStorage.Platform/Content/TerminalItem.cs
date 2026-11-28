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
using ProbablyStolen.DigitalStorage.Platform.Integration;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;

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
    private readonly SlotMetricsService _slotMetrics;
    private readonly TransferDiagnostics _transfers;
    private readonly CompatibilityDiagnostics _compatibility;
    private readonly IntegrationDecorationCatalog _decorations;
    private readonly string _modId;
    private readonly ModText _text;
    private readonly ItemAppearance _appearance;
    private readonly MachineContentWindow _windows;
    private readonly GridNetworkPowerCoordinator _networkPower;
    private readonly Func<bool> _identityLogEnabled;
    private readonly PlatformLogs _logs;
    private readonly NativeNetworkEvents _events;
    private readonly TerminalSearchService _search;
    private readonly ITerminalViewSettings _view;
    private readonly TerminalRegistry _registry;
    private readonly Func<bool> _testViewEnabled;

    internal TerminalItem(ModText text, ItemAppearance appearance, MachineContentWindow windows,
        GridNetworkPowerCoordinator networkPower, Func<bool> identityLogEnabled, PlatformLogs logs,
        NativeNetworkEvents events,
        TerminalSearchService search, ITerminalViewSettings view, TerminalRegistry registry, Func<bool> testViewEnabled,
        ButtonIcons icons, SlotMetricsService slotMetrics, TransferDiagnostics transfers,
        CompatibilityDiagnostics compatibility, IntegrationDecorationCatalog decorations, string modId)
    {
        _slotMetrics = slotMetrics ?? throw new ArgumentNullException(nameof(slotMetrics));
        _modId = modId;
        _transfers = transfers;
        _compatibility = compatibility;
        _decorations = decorations ?? throw new ArgumentNullException(nameof(decorations));
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
        _identityLogEnabled = identityLogEnabled ?? throw new ArgumentNullException(nameof(identityLogEnabled));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
    }

    // Tooltip code of the game and other mods may rewrite the item it describes; the stored root stays as stored.
    private static RichText RenderStoredTooltip(GameItemElement root)
    {
        using var read = StoredItemChanges.Read(root);
        return root.GetTooltip();
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
                (entry, pieces, operation) =>
                    _networkPower.ExtractPiecesToPlayer(item, entry.Key, pieces, _view.ExtractionPriority, operation),
                (batch, target, operation) => _networkPower.ExtractBatch(item, batch, target, operation),
                TerminalFeatures.AttributeCaptures,
                (items, diagnostic) => _networkPower.StoreSelected(item, items, diagnostic),
                (source, amount, diagnostic) => _networkPower.StoreSingle(item, source, amount, diagnostic),
                (sources, amount) => _networkPower.StoreRefusals(item, sources, amount),
                key => _networkPower.FindStoredSample(item, key),
                key => _networkPower.Storage.FindPreviewSample(item, key), RenderStoredTooltip,
                _logs.Ui, _identityLogEnabled, _logs.Storage, _events.Subscribe, _search, _transfers,
                _decorations, _modId);
            new TerminalWindow(_registry, storage, _view, _testViewEnabled, _icons, _slotMetrics).Attach(item);
            new TerminalItemDrop(item, (sources, amount) => _networkPower.StoreRefusals(item, sources, amount),
                (source, amount) => _networkPower.StoreSingle(item, source, amount), _text,
                _logs.Ui.Sink(LogVerbosity.Warning, "terminal_drop")).Attach();
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }
}
