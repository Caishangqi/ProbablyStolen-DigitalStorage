using System;
using ProbablyStolen.DigitalStorage.Client.Initialization;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Initialization;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Energy;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Localization;
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
    private readonly CellTooltipPresenter _cellTooltipPresenter;
    private readonly StorageContentRegistration _content;
    private readonly CellPageAudit _pageAudit;
    private readonly StorageEventBus _events;
    private readonly DigitalStorageSettings _settings;
    private readonly StorageVendors _vendors;
    private readonly ItemTypeBackfill _typeBackfill;
    private bool _isEnabled;
    private bool _isDisposed;

    private PlatformComposition(ModLog log, StartupStatusPresenter statusPresenter, string modId,
        GridNetworkTooltipPresenter networkTooltipPresenter, CellTooltipPresenter cellTooltipPresenter,
        ItemAppearance appearance,
        StorageContentRegistration content, CellPageAudit pageAudit, StorageEventBus events,
        DigitalStorageSettings settings, StorageVendors vendors)
    {
        _events = events;
        _settings = settings;
        _vendors = vendors;
        _typeBackfill = new ItemTypeBackfill(log);
        _log = log;
        _statusPresenter = statusPresenter;
        _modId = modId;
        _networkTooltipPresenter = networkTooltipPresenter;
        _cellTooltipPresenter = cellTooltipPresenter;
        _appearance = appearance;
        _content = content;
        _pageAudit = pageAudit;
    }

    /// <summary>Creates the startup composition from the loader manifest.</summary>
    /// <param name="manifest">The manifest supplied by the game loader.</param>
    /// <returns>An initialized platform composition.</returns>
    /// <exception cref="ArgumentNullException">The manifest is null.</exception>
    /// <exception cref="InvalidOperationException">The manifest identity is invalid.</exception>
    public static PlatformComposition Create(ModManifest manifest)
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
        networkPowerSettings.ApplyStorageLimits();
        var networkScanner = new NativeNetworkScanner(networkPowerSettings,
            message => log.Error(message),
            drive => NativeCellResolver.Get(drive));
        var networkPower = new GridNetworkPowerCoordinator(networkScanner, message => log.Log(message));
        var eventBus = new StorageEventBus(message => log.Error(message));
        var networkEvents = new NativeNetworkEvents(eventBus);
        var cellTooltipPresenter = new CellTooltipPresenter(text);
        var networkTooltipPresenter = new GridNetworkTooltipPresenter(networkScanner, networkPower, text, cellTooltipPresenter);
        var windows = new MachineContentWindow(text, networkPower, networkEvents);
        var contentSynchronization = new ItemContentSynchronization(key => text.Get(key),
            message => log.Error(message));
        var component = new StorageComponentFactory(appearance, componentDefinition, recipe, contentSynchronization);
        var cell = new StorageCellItem(text, appearance, recipe, log, networkPowerSettings.GetShowCellPages,
            contentSynchronization);
        var housing = new MaterialItem(cell);
        var drive = new DriveItem(appearance, windows, inventory, networkEvents, contentSynchronization);
        var controller = new ControllerItem(appearance, windows, contentSynchronization);
        var terminal = new TerminalItem(text, appearance, windows, networkPower,
            networkPowerSettings.GetTerminalDiagnostics, log, networkEvents, contentSynchronization);
        var content = new StorageContentRegistration(terminal, drive, cell, controller, component, housing,
            contentSynchronization, log);
        var composition = new PlatformComposition(log, statusPresenter, identity!.Id,
            networkTooltipPresenter, cellTooltipPresenter, appearance, content, new CellPageAudit(text, log), eventBus,
            networkPowerSettings, new StorageVendors(text, networkPowerSettings.GetEngineerDailyChance, log));
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
            ModHook.OnModAssetsLoaded += OnAssetsLoaded;
            ModHook.OnModConfigApplied += OnConfigApplied;
            ModHook.OnGameLoadedNormal += OnGameLoaded;
            ModHook.OnGameLoadedLate += _pageAudit.Run;
            ModHook.OnGameLoadedLate += _typeBackfill.Run;
            ModHook.OnCreateTooltipLate += _networkTooltipPresenter.OnCreateTooltipLate;
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
        ModHook.OnModAssetsLoaded -= OnAssetsLoaded;
        ModHook.OnModConfigApplied -= OnConfigApplied;
        ModHook.OnGameLoadedNormal -= OnGameLoaded;
        ModHook.OnGameLoadedLate -= _pageAudit.Run;
        ModHook.OnGameLoadedLate -= _typeBackfill.Run;
        TerminalStorageWindow.CloseCurrent();
        PartitionConfigurationWindow.CloseCurrent();
        ModHook.OnCreateTooltipLate -= _networkTooltipPresenter.OnCreateTooltipLate;
        _vendors.Dispose();
        _content.Dispose();
        _events.Dispose();
    }

    private void OnGameLoaded()
    {
        TerminalStorageWindow.CloseCurrent();
        PartitionConfigurationWindow.CloseCurrent();
    }

    private void OnConfigApplied(string modId)
    {
        if (_isEnabled && string.Equals(modId, _modId, StringComparison.Ordinal))
        {
            _settings.ApplyStorageLimits();
            _settings.RefreshSectionNames();
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

        _settings.RefreshSectionNames();
        ValidateResources();
    }

    private void ValidateResources()
    {
        try
        {
            _appearance.EnsureReady(TerminalItem.SpriteKey);
            _appearance.EnsureReady(DriveItem.SpriteKey);
            _appearance.EnsureReady(StorageCellItem.SpriteKey);
            _appearance.EnsureReady(ControllerItem.SpriteKey);
            _appearance.EnsureReady(StorageComponentFactory.SpriteKey);
            _appearance.EnsureReady(MaterialItem.SpriteKey);
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: content resources are unavailable ({error.GetType().Name}).");
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
