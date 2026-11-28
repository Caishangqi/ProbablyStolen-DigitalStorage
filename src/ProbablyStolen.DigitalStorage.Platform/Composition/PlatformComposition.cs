using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Client.Initialization;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
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
using ProbablyStolen.DigitalStorage.Platform.Identity;
using ProbablyStolen.DigitalStorage.Platform.Integration;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using UnityEngine.SceneManagement;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

/// <summary>Owns native content registration and resource checks for one mod lifecycle.</summary>
public sealed class PlatformComposition : IDisposable
{
    private readonly ButtonIcons _buttonIcons;
    private readonly SlotMetricsService _slotMetrics;
    // Only for the startup and shutdown status lines, which players need in every report.
    private readonly ModLog _log;
    private readonly PlatformLogs _logs;
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
    private StoredRootChecks<GameItem, GameItem>? _storedRootChecks;
    private readonly DigitalStorageSettings _settings;
    private readonly StorageVendors _vendors;
    private readonly DigitalStorageNetwork _network;
    private readonly NetworkSprites _networkSprites;
    private readonly NativeCustomerCatalog _customers;
    private readonly TerminalSearchComposition _search;
    private readonly ItemTypeBackfill _typeBackfill;
    private readonly DigitalStorageContentService _contentApi;
    private readonly DigitalStorageRuntime _runtime;
    private readonly IoPortCoordinator _ioPortCoordinator;
    private IoPortFrameDriver? _ioPortFrames;
    private readonly CompatibilityDiagnostics _compatibility;
    private readonly IntegrationDecorationCatalog _decorations;
    private IntegrationHost? _integrations;
    private IDisposable? _moduleBayHooks;
    private bool _runtimeExposed;

    private IDisposable? _localeRegistration;
    private readonly PinyinLifecycleCoordinator _pinyinLifecycle;
    private bool _assetsReady;
    private bool _isEnabled;
    private bool _isDisposed;

    private PlatformComposition(ModLog log, PlatformLogs logs, StartupStatusPresenter statusPresenter, string modId,
        TooltipComposition tooltip, BasicCellHandler cellHandler,
        ItemAppearance appearance,
        StorageContentRegistration content, ItemContentService itemContent, CellHealth health, CellRecovery recovery, LegacyMigration migration, StorageEventBus events,
        DigitalStorageSettings settings, StorageVendors vendors, DigitalStorageNetwork network, NetworkSprites networkSprites,
        NativeCustomerCatalog customers,
        TerminalSearchComposition search, CellWorkbenchItem workbench,
        DigitalStorageContentService contentApi, DigitalStorageRuntime runtime, IoPortCoordinator ioPortCoordinator,
        ButtonIcons buttonIcons, SlotMetricsService slotMetrics, CompatibilityDiagnostics compatibility,
        IntegrationDecorationCatalog decorations)
    {
        _slotMetrics = slotMetrics ?? throw new ArgumentNullException(nameof(slotMetrics));
        _compatibility = compatibility;
        _decorations = decorations ?? throw new ArgumentNullException(nameof(decorations));
        _buttonIcons = buttonIcons;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _ioPortCoordinator = ioPortCoordinator ?? throw new ArgumentNullException(nameof(ioPortCoordinator));
        _contentApi = contentApi ?? throw new ArgumentNullException(nameof(contentApi));
        _events = events;
        _settings = settings;
        _vendors = vendors;
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _networkSprites = networkSprites ?? throw new ArgumentNullException(nameof(networkSprites));
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _typeBackfill = new ItemTypeBackfill(logs.Storage);
        PlatformLogs.Activate(logs);
        _log = log;
        _logs = logs;
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
            locale => _settings.GetSearchConfiguration(locale, _logs.Ui.Sink(LogVerbosity.Error, "search_config_invalid")),
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
        var plugins = IntegrationHost.Discover(pluginTypes ?? throw new ArgumentNullException(nameof(pluginTypes)), _compatibility.Report);
        _integrations = new IntegrationHost(plugins, new NativeIntegrationEnvironment(),
            new HarmonyIntegrationPatcher(_modId), _settings, _runtime, _compatibility.Report, _decorations,
            _buttonIcons.HasOverlay, _contentApi.RegisterBatch, _compatibility.PluginLog);
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
        var categories = new BuiltinLogCategories();
        var networkPowerSettings = new DigitalStorageSettings(identity!.Id, text, categories.Registry);
        var diagnosticLogger = new DiagnosticLogger(categories.Registry, networkPowerSettings.GetLogSettings,
            (level, message) =>
            {
                if (level == LogVerbosity.Error) log.Error(message);
                else if (level == LogVerbosity.Warning) log.Warning(message);
                else log.Log(message);
            });
        var logs = new PlatformLogs(diagnosticLogger, categories);
        var buttonIcons = new ButtonIcons(logs.Ui.Sink(LogVerbosity.Error, "button_icons_failed"));
        var appearance = new ItemAppearance(identity!);
        var componentDefinition = new StorageComponent();
        var recipe = new StorageCellDisassemblyRecipe(componentDefinition);
        var inventory = new DriveCellInventory(recipe);
        var slotMetrics = new SlotMetricsService(networkPowerSettings, logs.Ui.Sink(LogVerbosity.Warning, "slot_metrics"));
        var compatibility = new CompatibilityDiagnostics(diagnosticLogger, categories.Comp);
        var decorations = new IntegrationDecorationCatalog(notificationFailed: compatibility.ReportDecorationNotificationFailure);
        var diagnostics = new StorageDiagnostics(logs.Content.Sink(LogVerbosity.Error, "content_failure"), () => false);
        var cellDiagnostics = new CellDiagnostics(diagnosticLogger, categories.Storage);
        var search = new TerminalSearchComposition(identity.Id, text,
            networkPowerSettings.GetSearchConfiguration(null, logs.Ui.Sink(LogVerbosity.Error, "search_config_invalid")),
            logs.Ui.Sink(LogVerbosity.Error, "search_failed"), registerSearchAddons);
        var networkScanner = new NativeNetworkScanner(networkPowerSettings,
            logs.Network.Sink(LogVerbosity.Error, "network_scan_failed"),
            drive => NativeCellResolver.Get(drive));
        var cellHealth = new CellHealth(text, cellDiagnostics);
        var migration = new LegacyMigration(cellHealth, cellDiagnostics);
        var recovery = new CellRecovery(cellHealth, cellDiagnostics);
        var transferDiagnostics = new TransferDiagnostics(diagnosticLogger, categories.Transfer);
        var networkPower = new GridNetworkPowerCoordinator(networkScanner, logs.Transfer.Sink(LogVerbosity.Info, "store_rejected"),
            cell =>
        {
            // Migration failures during play need the same warning as failures during loading.
            var migrated = migration.TryMigrate(cell);
            if (!migrated) cellHealth.WarnDoNotSave(1, "failed to migrate");
            return migrated;
        }, cellDiagnostics);
        var eventBus = new StorageEventBus(logs.Network.Sink(LogVerbosity.Error, "event_handler_failed"));
        var networkEvents = new NativeNetworkEvents(eventBus);
        var cellHandler = new BasicCellHandler(text);
        // Entry names and search text depend on the language and settings; both changes are posted as ConfigChanged.
        eventBus.Subscribe<NetworkChanged>(change =>
        {
            if (change.Reason == NetworkChangeReason.ConfigChanged) networkPower.Storage.ForgetEntryTexts();
        });
        var contentApi = new DigitalStorageContentService(logs.Content.Sink(LogVerbosity.Warning, "content_api"), diagnostics);
        var windows = new MachineContentWindow(text, networkPower, networkEvents);
        var contentSynchronization = new ItemContentSynchronization(ItemContentDefinitions.All, key => text.Get(key),
            logs.Content.Sink(LogVerbosity.Error, "item_content_failed"),
            (state, price) => ItemValueAdjustment.Adjust(state, price, contentApi.MaximumStage));
        var itemContent = new ItemContentService(contentSynchronization, () =>
        {
            search.Service.UpdateConfiguration(search.Service.Configuration);
            PartitionWindow.InvalidateSamples();
            eventBus.Post(new NetworkChanged(NetworkChangeReason.ContentsChanged));
        }, logs.Content.Sink(LogVerbosity.Error, "item_content_failed"), diagnostics);
        var component = new StorageComponentFactory(appearance, componentDefinition, recipe);
        var cell = new StorageCellItem(text, appearance, recipe, logs.Storage, networkPowerSettings.GetShowCellPages,
            itemContent.Changed);
        var housing = new MaterialItem(cell);
        var drive = new DriveItem(appearance, windows, inventory, networkEvents, text,
            () => DriveWindow.Create(text, networkPowerSettings, buttonIcons, slotMetrics, logs.Ui));
        var controller = new ControllerItem(appearance, windows);
        var terminalRegistry = TerminalFeatures.Create(networkPowerSettings, logs.Ui.Sink(LogVerbosity.Verbose, "terminal_trace"),
            networkPowerSettings.GetItemIdentityLog, networkPowerSettings.GetTerminalKeepOpen,
            networkPowerSettings.SetTerminalKeepOpen, () => networkPowerSettings.ExtractionPriority,
            value => networkPowerSettings.ExtractionPriority = value);
        var terminal = new TerminalItem(text, appearance, windows, networkPower,
            networkPowerSettings.GetItemIdentityLog, logs, networkEvents,
            search.Service,
            networkPowerSettings, terminalRegistry, networkPowerSettings.GetTerminalTestView, buttonIcons, slotMetrics,
            transferDiagnostics,
            compatibility, decorations, identity.Id);
        var workbench = new CellWorkbenchItem(text, appearance,
            target => PartitionWindow.Open(text, target, networkPowerSettings, search.Service, logs.Ui,
                buttonIcons, slotMetrics),
            PartitionWindow.CloseFor,
            logs.Ui.Sink(LogVerbosity.Warning, "workbench_failed"));
        var modules = new CellModuleFactory(appearance, itemContent.Changed, text, logs.Content.Sink(LogVerbosity.Error, "module_failed"));
        itemContent.SetModulePresenter(modules.Present);
        var goods = new StorageGoods(modules);
        var networkSprites = new NetworkSprites(logs.Content);
        var customers = new NativeCustomerCatalog(logs.Content);
        var coordinator = new IoPortCoordinator(networkScanner, networkPower.Storage, transferDiagnostics, networkEvents,
            networkPowerSettings, () => UnityEngine.Time.frameCount);
        var tooltip = new TooltipComposition(networkScanner, networkPower, text, cellHandler, workbench.FindUsage,
            logs.Ui.Sink(LogVerbosity.Error, "tooltip_failed"),
            () => new IoPortTooltipContributor(text, networkPowerSettings, coordinator.ReadDisplay));
        var ioPort = new IoPortItem(appearance, windows,
            _ => IoPortWindow.Create(text, networkPowerSettings, buttonIcons, slotMetrics, coordinator, logs.Ui),
            coordinator, text);
        var content = new StorageContentRegistration(terminal, drive, cell, controller, component, housing, workbench,
            modules, new TerminalModuleFactory(appearance), ioPort, itemContent, logs.Content);
        var composition = new PlatformComposition(log, logs, statusPresenter, identity!.Id,
            tooltip, cellHandler, appearance, content, itemContent, cellHealth, recovery, migration, eventBus,
            networkPowerSettings, new StorageVendors(text, logs.Content, goods, customers),
            new DigitalStorageNetwork(identity.Id, text, goods, logs.Content), networkSprites, customers, search, workbench,
            contentApi,
            new DigitalStorageRuntime(networkPower, networkScanner, slotMetrics, logs.Comp.Sink(LogVerbosity.Warning, "public_api"),
                () => eventBus.Post(new NetworkChanged(NetworkChangeReason.ContentsChanged))), coordinator, buttonIcons,
            slotMetrics, compatibility, decorations);
        log.Log(statusPresenter.Present(ModuleStartupPhase.Initialized));
        return composition;
    }

    /// <summary>Groups this mod's logging controls in an already constructed native settings window.</summary>
    public void GroupLogSettings(LoadedMod? mod, CustomUIWindow? window)
    {
        if (_isEnabled && mod?.Manifest?.ID == "digital_storage" && window is { IsAlive: true })
            _settings.GroupLogSettings(window);
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
        _contentApi.AddBakeCallback(DigitalStorageRegistries.VolatileItemFeature, registry =>
            VolatileFeatureRules.Publish(new VolatileFeatureRules(registry.Entries.Select(entry => entry.Value.IdentifierPrefix))));
        _isEnabled = true;
        try
        {
            _moduleBayHooks = ModuleBayExpansionHooks.Attach(_contentApi);
            _tooltip.Enable(() => _buttonIcons.Frame("window_frame", 4));
            _content.Enable();
            _vendors.Enable();
            _network.Enable();
            _localeRegistration = LocaleSelectionBridge.RegisterSettled(OnLocaleConfirmed,
                _logs.Ui.Sink(LogVerbosity.Error, "locale_failed"));
            ModHook.OnModAssetsLoaded += OnAssetsLoaded;
            ModHook.OnModConfigApplied += OnConfigApplied;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            ModHook.OnGameLoadedInit += _ioPortCoordinator.EndRun;
            ModHook.OnGameLoadedInit += _itemContent.BeginLoad;
            ModHook.OnNewGameStarted += _itemContent.NewGame;
            ModHook.OnNewGameStarted += OnIoPortNewGameStarted;
            ModHook.OnGameLoadedNormal += OnGameLoaded;
            // Order matters: audit the restored pages, backfill item types, then migrate pending cells.
            ModHook.OnGameLoadedLate += _health.Run;
            ModHook.OnGameLoadedLate += _typeBackfill.Run;
            ModHook.OnGameLoadedLate += _migration.Run;
            ModHook.OnGameLoadedLate += _recovery.Run;
            ModHook.OnGameLoadedLate += _itemContent.FinishLoad;
            ModHook.OnGameLoadedLate += StartIoPortRun;
            _ioPortFrames = IoPortFrameDriver.Create(_ioPortCoordinator.OnFrame,
                error => _logs.Transfer.Error("io_port_frame_failed", $"IO Port frame step failed ({error.GetType().Name})."));
            _storedRootChecks = new StoredRootChecks<GameItem, GameItem>(
                check => ModHelper.RunOnMainThread(_modId, check), StoredItemChanges.StillMatches, CellInventoryCache.ForgetWithRoots,
                () => _events.Post(new NetworkChanged(NetworkChangeReason.StoredItemChanged)));
            StoredItemChanges.Activate(new StoredItemChanges(StoredItemChanges.Locate, () => SaveManager.IsDecoding,
                _storedRootChecks));
            ExposeRuntime(_runtime);
            ExposeTooltipService(_tooltip.Service);
            ExposeContent(_contentApi);
            // Subscribed before the integrations, so a plugin ending a stopped run finds no cells of the previous game.
            ModHook.OnGameLoadedInit += _runtime.OnGameLoading;
            if (_integrations != null)
            {
                ModHook.OnGameLoadedInit += _integrations.OnGameLoaded;
                ModHook.OnNewGameStarted += OnIntegrationNewGameStarted;
                ModLoader.OnTrustRefreshed += _integrations.OnDependenciesChanged;
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
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        ModHook.OnGameLoadedInit -= _ioPortCoordinator.EndRun;
        ModHook.OnGameLoadedInit -= _itemContent.BeginLoad;
        ModHook.OnNewGameStarted -= _itemContent.NewGame;
        ModHook.OnNewGameStarted -= OnIoPortNewGameStarted;
        ModHook.OnGameLoadedNormal -= OnGameLoaded;
        ModHook.OnGameLoadedLate -= _health.Run;
        _health.Detach();
        ModHook.OnGameLoadedLate -= _typeBackfill.Run;
        ModHook.OnGameLoadedLate -= _migration.Run;
        ModHook.OnGameLoadedLate -= _recovery.Run;
        ModHook.OnGameLoadedLate -= _itemContent.FinishLoad;
        ModHook.OnGameLoadedLate -= StartIoPortRun;
        _ioPortFrames?.Destroy();
        _ioPortFrames = null;
        TerminalWindow.CloseCurrent();
        PartitionWindow.CloseCurrent();
        _buttonIcons.Dispose();
        ModHook.OnGameLoadedInit -= _runtime.OnGameLoading;
        if (_integrations != null)
        {
            ModHook.OnGameLoadedInit -= _integrations.OnGameLoaded;
            ModHook.OnNewGameStarted -= OnIntegrationNewGameStarted;
            ModLoader.OnTrustRefreshed -= _integrations.OnDependenciesChanged;
            _integrations.Dispose();
        }
        _settings.ClearIntegrationUiStates();

        if (_runtimeExposed) ExposeRuntime(null);
        ExposeTooltipService(null);
        ExposeContent(null);
        _moduleBayHooks?.Dispose();
        _moduleBayHooks = null;
        StoredItemChanges.Activate(null);
        _storedRootChecks?.Close();
        _storedRootChecks = null;
        _contentApi.Dispose();
        VolatileFeatureRules.Reset();
        StoredRootKeys.Clear();
        if (PlatformLogs.Current == _logs) PlatformLogs.Activate(null);
        _vendors.Dispose();
        _network.Dispose();
        _networkSprites.Dispose();
        _customers.Dispose();
        _content.Dispose();
        _itemContent.Dispose();
        _ioPortCoordinator.Dispose();
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
            _logs.Comp.Warning("public_api_exposure_failed", $"the public API could not be {(runtime == null ? "withdrawn" : "exposed")} " +
                $"({error.GetType().Name}: {error.Message}).");
        }
    }

    private void OnIoPortNewGameStarted(string scenarioId) => StartIoPortRun();

    private void StartIoPortRun()
    {
        if (!_isEnabled || _isDisposed || !PlayerStore.HasInstance || EmporiumEntry.Instance == null)
        {
            _ioPortCoordinator.EndRun();
            return;
        }

        _ioPortCoordinator.StartRun(PlayerStore.Instance, StoreStation.Instance);
    }

    private void OnGameLoaded()
    {
        TerminalWindow.CloseCurrent();
        PartitionWindow.CloseCurrent();
    }

    private void OnIntegrationNewGameStarted(string scenarioId)
    {
        _runtime.OnGameLoading();
        _integrations?.OnNewGameStarted(scenarioId);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene.name != "InventoryScene") return;
        _ioPortCoordinator.EndRun();
        _integrations?.OnGameUnloaded();
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
            _integrations?.OnSettingsApplied();
            if (_settings.ReadBuiltInChanges())
            {
                // Open windows rebuild when a slot, margin or dock button size changed.
                _slotMetrics.Refresh();
                _pinyinLifecycle.OnConfigApplied();
                _logs.Ui.Info("config_applied", "configuration applied; open network views refresh now.");
                _events.Post(new NetworkChanged(NetworkChangeReason.ConfigChanged));
            }
        }
    }

    private void OnAssetsLoaded(LoadedMod mod)
    {
        if (!_isEnabled || mod == null || mod.Manifest.ID != _modId)
        {
            return;
        }

        _buttonIcons.Load(mod);
        // Every mod is listed now. The integration host also needs our loaded textures for registration.
        try
        {
            _integrations?.OnModsLoaded();
        }
        catch (Exception error)
        {
            _logs.Comp.Warning("integrations_unavailable", $"mod integrations could not register ({error.GetType().Name}: {error.Message}).");
        }

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
            _logs.Content.Info("registries_frozen", "content.v2 registries frozen.");
        }
        catch (Exception error)
        {
            _logs.Content.Error("registries_unavailable", $"content registries are unavailable ({error.GetType().Name}: {error.Message}).");
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
            _logs.Comp.Warning("tooltip_api_exposure_failed", $"the tooltip API could not be {(service == null ? "withdrawn" : "exposed")} ({error.GetType().Name}: {error.Message}).");
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
            _logs.Comp.Warning("content_api_exposure_failed", $"content.v1 could not be {(content == null ? "withdrawn" : "exposed")} ({error.GetType().Name}: {error.Message}).");
        }
    }
    private void ValidateResources()
    {
        try
        {
            _appearance.EnsureReady(TerminalItem.SpriteKey);
            _appearance.EnsureReady(DriveItem.SpriteKey);
            _appearance.EnsureReady(ControllerItem.SpriteKey);
            _appearance.EnsureReady(IoPortItem.SpriteKey);
            _appearance.EnsureReady(MaterialItem.SpriteKey);
            foreach (var tier in StorageCellTier.All)
            {
                _appearance.EnsureReady("item_storage_cell_" + tier.Name);
                _appearance.EnsureReady("cell_component_" + tier.Name);
            }
        }
        catch (Exception error)
        {
            _logs.Content.Error("content_resources_unavailable", $"content resources are unavailable ({error.GetType().Name}: {error.Message}).");
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
