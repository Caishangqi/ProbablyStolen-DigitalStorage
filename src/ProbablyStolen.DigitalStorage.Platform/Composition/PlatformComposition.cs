using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Client.Initialization;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Initialization;
using ProbablyStolen.DigitalStorage.Platform.Api;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Content;
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
    private readonly ModLog _log;
    private readonly StartupStatusPresenter _statusPresenter;
    private readonly string _modId;
    private readonly ItemAppearance _appearance;
    private readonly GridNetworkTooltipPresenter _networkTooltipPresenter;
    private readonly BasicCellHandler _cellHandler;
    private readonly StorageContentRegistration _content;
    private readonly ItemContentService _itemContent;
    private readonly CellWorkbenchItem _workbench;
    private readonly CellHealth _health;
    private readonly LegacyMigration _migration;
    private readonly StorageEventBus _events;
    private readonly DigitalStorageSettings _settings;
    private readonly StorageVendors _vendors;
    private readonly TerminalSearchComposition _search;
    private readonly ItemTypeBackfill _typeBackfill;
    private readonly DigitalStorageRuntime _runtime;
    private IntegrationHost? _integrations;
    private bool _runtimeExposed;
    private IDisposable? _localeRegistration;
    private readonly PinyinLifecycleCoordinator _pinyinLifecycle;
    private bool _assetsReady;
    private bool _isEnabled;
    private bool _isDisposed;

    private PlatformComposition(ModLog log, StartupStatusPresenter statusPresenter, string modId,
        GridNetworkTooltipPresenter networkTooltipPresenter, BasicCellHandler cellHandler,
        ItemAppearance appearance,
        StorageContentRegistration content, ItemContentService itemContent, CellHealth health, LegacyMigration migration, StorageEventBus events,
        DigitalStorageSettings settings, StorageVendors vendors, TerminalSearchComposition search, CellWorkbenchItem workbench,
        DigitalStorageRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _events = events;
        _settings = settings;
        _vendors = vendors;
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _typeBackfill = new ItemTypeBackfill(log);
        _log = log;
        _statusPresenter = statusPresenter;
        _modId = modId;
        _networkTooltipPresenter = networkTooltipPresenter;
        _cellHandler = cellHandler;
        _appearance = appearance;
        _content = content;
        _itemContent = itemContent;
        _workbench = workbench;
        _health = health;
        _migration = migration;
        _pinyinLifecycle = new PinyinLifecycleCoordinator(
            locale => _settings.GetSearchConfiguration(locale, message => _log.Error(message)),
            _search.Update, _search.Service.Configuration.PinyinEnabled);
    }

    /// <summary>Gets the public API entry; valid until the composition is disposed.</summary>
    public IDigitalStorageRuntime Runtime => _runtime;

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
        var appearance = new ItemAppearance(identity!);
        var componentDefinition = new StorageComponent();
        var recipe = new StorageCellDisassemblyRecipe(componentDefinition);
        var inventory = new DriveCellInventory(recipe);
        var networkPowerSettings = new DigitalStorageSettings(identity!.Id, text);
        var search = new TerminalSearchComposition(identity.Id, text,
            networkPowerSettings.GetSearchConfiguration(null, message => log.Error(message)),
            message => log.Error(message), registerSearchAddons);
        var networkScanner = new NativeNetworkScanner(networkPowerSettings,
            message => log.Error(message),
            drive => NativeCellResolver.Get(drive));
        var cellHealth = new CellHealth(text, log);
        var migration = new LegacyMigration(cellHealth, log);
        var networkPower = new GridNetworkPowerCoordinator(networkScanner, message => log.Log(message), cell =>
        {
            // Migration failures during play need the same warning as failures during loading.
            var migrated = migration.TryMigrate(cell);
            if (!migrated) cellHealth.WarnDoNotSave(1, "failed to migrate");
            return migrated;
        });
        var eventBus = new StorageEventBus(message => log.Error(message));
        var networkEvents = new NativeNetworkEvents(eventBus);
        var cellHandler = new BasicCellHandler(text);
        var networkTooltipPresenter = new GridNetworkTooltipPresenter(networkScanner, networkPower, text, cellHandler);
        var windows = new MachineContentWindow(text, networkPower, networkEvents);
        var contentSynchronization = new ItemContentSynchronization(ItemContentDefinitions.All, key => text.Get(key),
            message => log.Error(message), ItemValueAdjustment.Adjust);
        var itemContent = new ItemContentService(contentSynchronization, () =>
        {
            search.Service.UpdateConfiguration(search.Service.Configuration);
            PartitionWindow.InvalidateSamples();
            eventBus.Post(new NetworkChanged(NetworkChangeReason.ContentsChanged));
        }, message => log.Error(message));
        var component = new StorageComponentFactory(appearance, componentDefinition, recipe);
        var cell = new StorageCellItem(text, appearance, recipe, log, networkPowerSettings.GetShowCellPages,
            itemContent.Changed);
        var housing = new MaterialItem(cell);
        var drive = new DriveItem(appearance, windows, inventory, networkEvents);
        var controller = new ControllerItem(appearance, windows);
        var terminal = new TerminalItem(text, appearance, windows, networkPower,
            networkPowerSettings.GetTerminalDiagnostics, log, networkEvents, search.Service,
            networkPowerSettings);
        var workbench = new CellWorkbenchItem(text, appearance,
            target => PartitionWindow.Open(text, target, networkPowerSettings, search.Service, message => log.Log(message)),
            PartitionWindow.CloseFor,
            message => log.Log(message));
        var modules = new CellModuleFactory(appearance, itemContent.Changed, text);
        itemContent.SetModulePresenter(modules.Present);
        var content = new StorageContentRegistration(terminal, drive, cell, controller, component, housing, workbench,
            modules, itemContent, log);
        var composition = new PlatformComposition(log, statusPresenter, identity!.Id,
            networkTooltipPresenter, cellHandler, appearance, content, itemContent, cellHealth, migration, eventBus,
            networkPowerSettings, new StorageVendors(text, networkPowerSettings.GetEngineerDailyChance, log, modules), search, workbench,
            new DigitalStorageRuntime(networkPower, networkScanner, message => log.Warning(message),
                () => eventBus.Post(new NetworkChanged(NetworkChangeReason.ContentsChanged))));
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

        _isEnabled = true;
        try
        {
            _content.Enable();
            _vendors.Enable();
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
            ModHook.OnGameLoadedLate += _itemContent.FinishLoad;
            ModHook.OnCreateTooltipLate += _networkTooltipPresenter.OnCreateTooltipLate;
            ExposeRuntime(_runtime);
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
        ModHook.OnGameLoadedLate -= _itemContent.FinishLoad;
        TerminalStorageWindow.CloseCurrent();
        PartitionWindow.CloseCurrent();
        ModHook.OnCreateTooltipLate -= _networkTooltipPresenter.OnCreateTooltipLate;
        ModHook.OnGameLoadedInit -= _runtime.OnGameLoading;
        if (_integrations != null)
        {
            ModHook.OnGameLoadedInit -= _integrations.OnGameLoaded;
            _integrations.Dispose();
        }

        if (_runtimeExposed) ExposeRuntime(null);
        _vendors.Dispose();
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
        TerminalStorageWindow.CloseCurrent();
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

        _assetsReady = true;
        _search.MarkAssetsReady();
        _settings.RefreshSectionNames();
        ValidateResources();
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
