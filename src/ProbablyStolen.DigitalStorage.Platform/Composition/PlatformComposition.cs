using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Client.Initialization;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Initialization;
using ProbablyStolen.DigitalStorage.Platform.Api;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Energy;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Integration;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

/// <summary>Owns native content registration and resource checks for one mod lifecycle.</summary>
public sealed class PlatformComposition : IDisposable
{
    private readonly ButtonIcons _buttonIcons;
    private readonly ModLog _log;
    private readonly StartupStatusPresenter _statusPresenter;
    private readonly string _modId;
    private readonly ItemAppearance _appearance;
    private readonly TooltipComposition _tooltip;
    private readonly BasicCellHandler _cellHandler;
    private readonly StorageContentRegistration _content;
    private readonly ItemContentService _itemContent;
    private readonly CellWorkbenchItem _workbench;
    private readonly CellHealth _health;
    private readonly CellRecovery _recovery;
    private readonly LegacyMigration _migration;
    private readonly StorageEventBus _events;
    private readonly DigitalStorageSettings _settings;
    private readonly StorageVendors _vendors;
    private readonly DigitalStorageNetwork _network;
    private readonly NetworkSprites _networkSprites;
    private readonly NativeCustomerCatalog _customers;
    private readonly TerminalSearchComposition _search;
    private readonly ItemTypeBackfill _typeBackfill;
    private readonly DigitalStorageContentService _contentApi;
    private readonly DigitalStorageRuntime _runtime;
    private IntegrationHost? _integrations;
    private IDisposable? _moduleBayHooks;
    private bool _runtimeExposed;

    private IDisposable? _localeRegistration;
    private readonly PinyinLifecycleCoordinator _pinyinLifecycle;
    private bool _assetsReady;
    private bool _isEnabled;
    private bool _isDisposed;

    private PlatformComposition(ModLog log, StartupStatusPresenter statusPresenter, string modId,
        TooltipComposition tooltip, BasicCellHandler cellHandler,
        ItemAppearance appearance,
        StorageContentRegistration content, ItemContentService itemContent, CellHealth health, CellRecovery recovery, LegacyMigration migration, StorageEventBus events,
        DigitalStorageSettings settings, StorageVendors vendors, DigitalStorageNetwork network, NetworkSprites networkSprites,
        NativeCustomerCatalog customers,
        TerminalSearchComposition search, CellWorkbenchItem workbench,
        DigitalStorageContentService contentApi, DigitalStorageRuntime runtime, ButtonIcons buttonIcons)
    {
        _buttonIcons = buttonIcons;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _contentApi = contentApi ?? throw new ArgumentNullException(nameof(contentApi));
        _events = events;
        _settings = settings;
        _vendors = vendors;
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _networkSprites = networkSprites ?? throw new ArgumentNullException(nameof(networkSprites));
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _typeBackfill = new ItemTypeBackfill(log);
        _log = log;
        _statusPresenter = statusPresenter;
        _modId = modId;
        _tooltip = tooltip;
        _cellHandler = cellHandler;
        _appearance = appearance;
        _content = content;
        _itemContent = itemContent;
        _workbench = workbench;
        _health = health;
        _recovery = recovery;
        _migration = migration;
        _pinyinLifecycle = new PinyinLifecycleCoordinator(
            locale => _settings.GetSearchConfiguration(locale, message => _log.Error(message)),
            _search.Update, _search.Service.Configuration.PinyinEnabled);
    }

    /// <summary>Gets the public API entry; valid until the composition is disposed.</summary>
    public IDigitalStorageRuntime Runtime => _runtime;

    /// <summary>Gets the content.v1 registration and read-only API for this lifecycle.</summary>
    public IDigitalStorageContentBootstrap ContentBootstrap => _contentApi;

    /// <summary>
    /// Discovers the integration plugins among <paramref name="pluginTypes"/> and keeps those whose mod is enabled.
    /// Call once from the mod's Init, before <see cref="Enable"/>, passing <c>typeof(T).Assembly.GetTypes()</c> of the
    /// plugin assembly.
    /// </summary>
    /// <exception cref="InvalidOperationException">Integrations were already attached or the composition is enabled.</exception>
    public void AttachIntegrations(IEnumerable<Type> pluginTypes)
    {
        ThrowIfDisposed();
        if (_integrations != null || _isEnabled) throw new InvalidOperationException("Attach integrations once, before enabling.");
        var plugins = IntegrationHost.Discover(pluginTypes ?? throw new ArgumentNullException(nameof(pluginTypes)), _log.Warning);
        _integrations = new IntegrationHost(plugins, new NativeIntegrationEnvironment(),
            new HarmonyIntegrationPatcher(_modId), _settings, _runtime, message => _log.Log(message));
        _integrations.Initialize();
    }

    /// <summary>Creates the startup composition from the loader manifest.</summary>
    /// <param name="manifest">The manifest supplied by the game loader.</param>
    /// <returns>An initialized platform composition.</returns>
    /// <exception cref="ArgumentNullException">The manifest is null.</exception>
    /// <exception cref="InvalidOperationException">The manifest identity is invalid.</exception>
    public static PlatformComposition Create(ModManifest manifest) => Create(manifest, null);

    /// <summary>Creates a composition with explicit search addon registrations, before any terminal is opened.</summary>
    /// <param name="manifest">The loader manifest.</param>
    /// <param name="registerSearchAddons">Registers matchers, detached document fields and prefixes for this lifecycle.</param>
    /// <returns>An initialized platform composition.</returns>
    public static PlatformComposition Create(ModManifest manifest, Action<SearchMatcherRegistry>? registerSearchAddons)
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var log = new ModLog(manifest);
        CellPageRestore.SetModId(manifest.ID);
        if (!ModuleIdentity.TryCreate(manifest.ID, manifest.Name, out var identity, out var error))
        {
            var message = error ?? "The module identity is invalid.";
            log.Error(message);
            throw new InvalidOperationException(message);
        }

        var statusPresenter = new StartupStatusPresenter(identity!);
        var text = new ModText(identity!);
        var buttonIcons = new ButtonIcons(message => log.Error(message));
        var appearance = new ItemAppearance(identity!);
        var componentDefinition = new StorageComponent();
        var recipe = new StorageCellDisassemblyRecipe(componentDefinition);
        var inventory = new DriveCellInventory(recipe);
        var networkPowerSettings = new DigitalStorageSettings(identity!.Id, text);
        var diagnostics = new StorageDiagnostics(message => log.Error(message), networkPowerSettings.GetStorageDiagnostics,
            message => log.Log(message));
        var search = new TerminalSearchComposition(identity.Id, text,
            networkPowerSettings.GetSearchConfiguration(null, message => log.Error(message)),
            message => log.Error(message), registerSearchAddons);
        var networkScanner = new NativeNetworkScanner(networkPowerSettings,
            message => log.Error(message),
            drive => NativeCellResolver.Get(drive));
        var cellHealth = new CellHealth(text, log);
        var migration = new LegacyMigration(cellHealth, log);
        var recovery = new CellRecovery(cellHealth, diagnostics, log);
        var networkPower = new GridNetworkPowerCoordinator(networkScanner, message => log.Log(message), cell =>
        {
            // Migration failures during play need the same warning as failures during loading.
            var migrated = migration.TryMigrate(cell);
            if (!migrated) cellHealth.WarnDoNotSave(1, "failed to migrate");
            return migrated;
        }, diagnostics);
        var eventBus = new StorageEventBus(message => log.Error(message));
        var networkEvents = new NativeNetworkEvents(eventBus);
        var cellHandler = new BasicCellHandler(text);
        // Entry names and search text depend on the language and settings; both changes are posted as ConfigChanged.
        eventBus.Subscribe<NetworkChanged>(change =>
        {
            if (change.Reason == NetworkChangeReason.ConfigChanged) networkPower.Storage.ForgetEntryTexts();
        });
        var tooltip = new TooltipComposition(networkScanner, networkPower, text, cellHandler, message => log.Error(message));
        var contentApi = new DigitalStorageContentService(message => log.Warning(message), diagnostics);
        var windows = new MachineContentWindow(text, networkPower, networkEvents);
        var contentSynchronization = new ItemContentSynchronization(ItemContentDefinitions.All, key => text.Get(key),
            message => log.Error(message), (state, price) => ItemValueAdjustment.Adjust(state, price, contentApi.MaximumStage));
        var itemContent = new ItemContentService(contentSynchronization, () =>
        {
            search.Service.UpdateConfiguration(search.Service.Configuration);
            PartitionWindow.InvalidateSamples();
            eventBus.Post(new NetworkChanged(NetworkChangeReason.ContentsChanged));
        }, message => log.Error(message), diagnostics);
        var component = new StorageComponentFactory(appearance, componentDefinition, recipe);
        var cell = new StorageCellItem(text, appearance, recipe, log, networkPowerSettings.GetShowCellPages,
            itemContent.Changed);
        var housing = new MaterialItem(cell);
        var drive = new DriveItem(appearance, windows, inventory, networkEvents);
        var controller = new ControllerItem(appearance, windows);
        var terminalRegistry = TerminalFeatures.Create(networkPowerSettings, message => log.Log(message),
            networkPowerSettings.GetItemIdentityLog, networkPowerSettings.GetTerminalKeepOpen,
            networkPowerSettings.SetTerminalKeepOpen);
        var terminal = new TerminalItem(text, appearance, windows, networkPower,
            networkPowerSettings.GetTerminalDiagnostics, networkPowerSettings.GetItemIdentityLog, log, networkEvents,
            search.Service,
            networkPowerSettings, terminalRegistry, networkPowerSettings.GetTerminalTestView, buttonIcons);
        var workbench = new CellWorkbenchItem(text, appearance,
            target => PartitionWindow.Open(text, target, networkPowerSettings, search.Service, message => log.Log(message), buttonIcons),
            PartitionWindow.CloseFor,
            message => log.Log(message));
        var modules = new CellModuleFactory(appearance, itemContent.Changed, text);
        itemContent.SetModulePresenter(modules.Present);
        var goods = new StorageGoods(modules);
        var networkSprites = new NetworkSprites(log);
        var customers = new NativeCustomerCatalog(log);
        var content = new StorageContentRegistration(terminal, drive, cell, controller, component, housing, workbench,
            modules, new TerminalModuleFactory(appearance), itemContent, log);
        var composition = new PlatformComposition(log, statusPresenter, identity!.Id,
            tooltip, cellHandler, appearance, content, itemContent, cellHealth, recovery, migration, eventBus,
            networkPowerSettings, new StorageVendors(text, log, goods, customers),
            new DigitalStorageNetwork(identity.Id, text, goods, log), networkSprites, customers, search, workbench,
            contentApi,
            new DigitalStorageRuntime(networkPower, networkScanner, message => log.Warning(message),
                () => eventBus.Post(new NetworkChanged(NetworkChangeReason.ContentsChanged))), buttonIcons);
        log.Log(statusPresenter.Present(ModuleStartupPhase.Initialized));
        return composition;
    }

    /// <summary>Records the enabled state once for this composition.</summary>
    /// <exception cref="ObjectDisposedException">The composition has been disposed.</exception>
    public void Enable()
    {
        ThrowIfDisposed();
        if (_isEnabled)
        {
            return;
        }

        _contentApi.RegisterBatch("digital_storage", batch =>
        {
            BuiltinModuleBays.Register(batch.Registries);
            _vendors.RegisterCustomers(batch.Registries);
        });
        _contentApi.AddBakeCallback(DigitalStorageRegistries.Customer, _customers.Bind);
        _isEnabled = true;
        try
        {
            _moduleBayHooks = ModuleBayExpansionHooks.Attach(_contentApi);
            _tooltip.Enable(() => _buttonIcons.Frame("window_frame", 4));
            _content.Enable();
            _vendors.Enable();
            _network.Enable();
            _localeRegistration = LocaleSelectionBridge.RegisterSettled(OnLocaleConfirmed, message => _log.Error(message));
            ModHook.OnModAssetsLoaded += OnAssetsLoaded;
            ModHook.OnModConfigApplied += OnConfigApplied;
            ModHook.OnGameLoadedInit += _itemContent.BeginLoad;
            ModHook.OnNewGameStarted += _itemContent.NewGame;
            ModHook.OnGameLoadedNormal += OnGameLoaded;
            // Order matters: audit the restored pages, backfill item types, then migrate pending cells.
            ModHook.OnGameLoadedLate += _health.Run;
            ModHook.OnGameLoadedLate += _typeBackfill.Run;
            ModHook.OnGameLoadedLate += _migration.Run;
            ModHook.OnGameLoadedLate += _recovery.Run;
            ModHook.OnGameLoadedLate += _itemContent.FinishLoad;
            ExposeRuntime(_runtime);
            ExposeTooltipService(_tooltip.Service);
            ExposeContent(_contentApi);
            // Subscribed before the integrations, so a plugin ending a stopped run finds no cells of the previous game.
            ModHook.OnGameLoadedInit += _runtime.OnGameLoading;
            if (_integrations != null)
            {
                ModHook.OnGameLoadedInit += _integrations.OnGameLoaded;
                _integrations.Enable();
            }

            _log.Log(_statusPresenter.Present(ModuleStartupPhase.Enabled));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Releases resources owned by this composition.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_isEnabled)
        {
            _log.Log(_statusPresenter.Present(ModuleStartupPhase.Disabled));
        }

        _isEnabled = false;
        _isDisposed = true;
        _tooltip.Dispose();
        _workbench.Detach();
        ModHook.OnModAssetsLoaded -= OnAssetsLoaded;
        _localeRegistration?.Dispose();
        _localeRegistration = null;
        ModHook.OnModConfigApplied -= OnConfigApplied;
        ModHook.OnGameLoadedInit -= _itemContent.BeginLoad;
        ModHook.OnNewGameStarted -= _itemContent.NewGame;
        ModHook.OnGameLoadedNormal -= OnGameLoaded;
        ModHook.OnGameLoadedLate -= _health.Run;
        _health.Detach();
        ModHook.OnGameLoadedLate -= _typeBackfill.Run;
        ModHook.OnGameLoadedLate -= _migration.Run;
        ModHook.OnGameLoadedLate -= _recovery.Run;
        ModHook.OnGameLoadedLate -= _itemContent.FinishLoad;
        TerminalWindow.CloseCurrent();
        PartitionWindow.CloseCurrent();
        _buttonIcons.Dispose();
        ModHook.OnGameLoadedInit -= _runtime.OnGameLoading;
        if (_integrations != null)
        {
            ModHook.OnGameLoadedInit -= _integrations.OnGameLoaded;
            _integrations.Dispose();
        }
        _settings.ClearIntegrationUiStates();

        if (_runtimeExposed) ExposeRuntime(null);
        ExposeTooltipService(null);
        ExposeContent(null);
        _moduleBayHooks?.Dispose();
        _moduleBayHooks = null;
        _contentApi.Dispose();
        _vendors.Dispose();
        _network.Dispose();
        _networkSprites.Dispose();
        _customers.Dispose();
        _content.Dispose();
        _itemContent.Dispose();
        _events.Dispose();
        _search.Dispose();
    }

    // Other mods that list this one as a prerequisite look the runtime up under its key; a stopped mod withdraws it.
    private void ExposeRuntime(IDigitalStorageRuntime? runtime)
    {
        try
        {
            ModHelper.Expose(_modId, DigitalStorageRuntimeKeys.RuntimeServiceKey, runtime);
            _runtimeExposed = runtime != null;
        }
        catch (Exception error)
        {
            _log.Warning($"Digital Storage: the public API could not be {(runtime == null ? "withdrawn" : "exposed")} " +
                $"({error.GetType().Name}: {error.Message}).");
        }
    }

    private void OnGameLoaded()
    {
        TerminalWindow.CloseCurrent();
        PartitionWindow.CloseCurrent();
    }

    private void OnLocaleConfirmed(string? locale, bool settled)
    {
        if (!_isEnabled || _isDisposed) return;
        _pinyinLifecycle.OnLocaleChanged(locale, settled);
        if (!settled) return;
        _itemContent.RefreshLocale();
        if (_assetsReady) _settings.RefreshSectionNames();
        _events.Post(new NetworkChanged(NetworkChangeReason.ConfigChanged));
    }

    private void OnConfigApplied(string modId)
    {
        if (_isEnabled && string.Equals(modId, _modId, StringComparison.Ordinal))
        {
            if (_assetsReady) _settings.RefreshSectionNames();
            _pinyinLifecycle.OnConfigApplied();
            _integrations?.OnSettingsApplied();
            _log.Log("Digital Storage: configuration applied; open network views refresh now.");
            _events.Post(new NetworkChanged(NetworkChangeReason.ConfigChanged));
        }
    }

    private void OnAssetsLoaded(LoadedMod mod)
    {
        if (!_isEnabled || mod == null || mod.Manifest.ID != _modId)
        {
            return;
        }

        // The loader loads mod assets only after every mod is listed, so other mods' versions are known now. Run it
        // first: a failure in the work below must not leave the integrations unregistered for the session.
        try
        {
            _integrations?.OnModsLoaded();
        }
        catch (Exception error)
        {
            _log.Warning($"Digital Storage: mod integrations could not register ({error.GetType().Name}: {error.Message}).");
        }

        _buttonIcons.Load(mod);
        _networkSprites.Load(mod);
        _assetsReady = true;
        FreezeContentRegistries();
        _search.MarkAssetsReady();
        _settings.RefreshSectionNames();
        ValidateResources();
    }

    private void FreezeContentRegistries()
    {
        try
        {
            _contentApi.Bootstrap(builtin: true);
            _log.Log("Digital Storage: content.v2 registries frozen.");
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: content registries are unavailable ({error.GetType().Name}: {error.Message}).");
        }
    }

    private void ExposeTooltipService(ITooltipService? service)
    {
        try
        {
            ModHelper.Expose(_modId, TooltipServiceKeys.ServiceKey, service);

        }
        catch (Exception error)
        {
            _log.Warning($"Digital Storage: the tooltip API could not be {(service == null ? "withdrawn" : "exposed")} ({error.GetType().Name}: {error.Message}).");
        }
    }
    private void ExposeContent(IDigitalStorageContentApi? content)
    {
        try
        {
            ModHelper.Expose(_modId, DigitalStorageContentKeys.ContentServiceKey, content);
        }
        catch (Exception error)
        {
            _log.Warning($"Digital Storage: content.v1 could not be {(content == null ? "withdrawn" : "exposed")} ({error.GetType().Name}: {error.Message}).");
        }
    }
    private void ValidateResources()
    {
        try
        {
            _appearance.EnsureReady(TerminalItem.SpriteKey);
            _appearance.EnsureReady(DriveItem.SpriteKey);
            _appearance.EnsureReady(ControllerItem.SpriteKey);
            _appearance.EnsureReady(MaterialItem.SpriteKey);
            foreach (var tier in StorageCellTier.All)
            {
                _appearance.EnsureReady("item_storage_cell_" + tier.Name);
                _appearance.EnsureReady("cell_component_" + tier.Name);
            }
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: content resources are unavailable ({error.GetType().Name}: {error.Message}).");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(PlatformComposition));
        }
    }
}
