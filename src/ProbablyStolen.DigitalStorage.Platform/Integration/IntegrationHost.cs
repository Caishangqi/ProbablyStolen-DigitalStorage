using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Platform.Integration;

/// <summary>
/// The loader lists a mod in <c>ModLoader.Mods</c> only after its <c>OnEnable</c> returns, and loads
/// mod assets only after every mod is listed (<c>ModLoader.Awake</c>: <c>LoadDiscoveredMods</c>, then
/// <c>LoadModAssets</c>), so the composition calls <see cref="OnModsLoaded"/> from this mod's assets loaded hook.
/// Each plugin failure disables only that plugin. A plugin whose patches cannot all be applied keeps none.
/// </summary>
internal sealed class IntegrationHost : IDisposable
{
    internal const string StatusIncompatible = "integration_state_incompatible";
    internal const string StatusNotRunning = "integration_state_not_running";
    internal const string StatusFaulted = "integration_state_failed";
    internal const string StatusUnverified = "integration_state_unverified";
    internal const string StatusActive = "integration_state_active";
    internal const string StatusActiveUnverified = "integration_state_active_unverified";
    internal const string StatusDisabled = "integration_state_disabled";
    internal const string StatusFailedLabel = "integration_state_failed_label";
    internal const string StatusNotRunningLabel = "integration_state_not_running_label";

    private readonly List<Entry> _entries;
    private readonly IIntegrationEnvironment _environment;
    private readonly IIntegrationPatcher _patcher;
    private readonly IIntegrationSettings _settings;
    private readonly IDigitalStorageRuntime _runtime;
    private readonly Action<IntegrationNotice> _log;
    private readonly IntegrationDecorationCatalog? _decorations;
    private readonly Func<string, bool>? _overlayAvailable;
    private bool _initialized;
    private bool _enabled;
    private bool _registered;
    private bool _gameLoaded;
    private bool _disposed;

    internal IntegrationHost(IEnumerable<IIntegrationPlugin> plugins, IIntegrationEnvironment environment,
        IIntegrationPatcher patcher, IIntegrationSettings settings, IDigitalStorageRuntime runtime,
        Action<IntegrationNotice> log, IntegrationDecorationCatalog? decorations = null,
        Func<string, bool>? overlayAvailable = null)
    {
        _entries = (plugins ?? throw new ArgumentNullException(nameof(plugins))).Select(plugin => new Entry(plugin)).ToList();
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _patcher = patcher ?? throw new ArgumentNullException(nameof(patcher));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _decorations = decorations;
        _overlayAvailable = overlayAvailable;
    }

    /// <summary>
    /// Creates plugins among <paramref name="types"/> that carry <see cref="IntegrationPluginAttribute"/>; a type
    /// that cannot be created is logged and skipped.
    /// </summary>
    /// <remarks>
    /// The caller must pass <c>typeof(T).Assembly.GetTypes()</c> of the plugin assembly literally so the mod scanner
    /// recognizes reflection on the mod's own types.
    /// </remarks>
    internal static IReadOnlyList<IIntegrationPlugin> Discover(IEnumerable<Type> types, Action<IntegrationNotice> log)
    {
        if (types == null) throw new ArgumentNullException(nameof(types));
        if (log == null) throw new ArgumentNullException(nameof(log));
        var plugins = new List<IIntegrationPlugin>();
        foreach (var type in types)
        {
            if (!Attribute.IsDefined(type, typeof(IntegrationPluginAttribute), inherit: false)) continue;
            try
            {
                if (type.IsAbstract || !typeof(IIntegrationPlugin).IsAssignableFrom(type))
                    throw new InvalidOperationException("it is not a concrete integration plugin");
                plugins.Add((IIntegrationPlugin)Activator.CreateInstance(type)!);
            }
            catch (Exception error)
            {
                log(new IntegrationNotice(IntegrationSeverity.Error, type.FullName ?? type.Name, string.Empty,
                    string.Empty, IntegrationState.Faulted, false, "discovery_failed", error.GetType().Name));
            }
        }

        return plugins;
    }

    /// <summary>Gets the state of a plugin, for diagnostics and tests.</summary>
    internal IntegrationState StateOf(string pluginUid) =>
        _entries.FirstOrDefault(entry => entry.Uid == pluginUid)?.State ?? IntegrationState.Absent;

    /// <summary>Keeps the plugins whose mod is enabled; call once from the mod's Init.</summary>
    internal void Initialize()
    {
        if (_initialized || _disposed) return;
        _decorations?.BindHost(OnDependenciesChanged, FaultDecoration);
        _initialized = true;
        foreach (var entry in _entries)
        {
            try
            {
                entry.Uid = entry.Plugin.PluginUid;
                entry.ModId = entry.Plugin.RequiredModId;
                if (string.IsNullOrWhiteSpace(entry.Uid) || string.IsNullOrWhiteSpace(entry.ModId))
                    throw new InvalidOperationException("the plugin names no uid or required mod");
                if (_entries.Count(other => other.Uid == entry.Uid) > 1)
                    throw new InvalidOperationException($"the uid {entry.Uid} is used twice");
                if (!_environment.IsModEnabled(entry.ModId)) continue;
                entry.State = IntegrationState.Disabled;
            }
            catch (Exception error)
            {
                Fault(entry, "initialize", error);
            }
        }
    }

    /// <summary>Allows the register phase; call from the mod's OnEnable.</summary>
    internal void Enable()
    {
        if (_disposed || !_initialized) return;
        _enabled = true;
    }

    /// <summary>Runs the register phase once every mod is loaded; later calls do nothing.</summary>
    internal void OnModsLoaded()
    {
        if (_enabled) RegisterAll();
    }

    /// <summary>Starts the same integration runtime scope for a freshly created game.</summary>
    internal void OnNewGameStarted(string _) => OnGameLoaded();

    /// <summary>Runs the runtime phases for a new or loaded game.</summary>
    internal void OnGameLoaded()
    {
        if (_disposed) return;
        foreach (var entry in _entries.Where(entry => entry.State == IntegrationState.Active))
        {
            if (!CheckRunning(entry)) continue;
            if (entry.RuntimeAvailable) RuntimeUnavailable(entry);
            if (entry.State == IntegrationState.Active) RuntimeAvailable(entry);
        }

        _gameLoaded = true;
    }

    /// <summary>Ends the run and releases its callbacks while keeping plugin registrations.</summary>
    internal void OnGameUnloaded()
    {
        if (_disposed || !_gameLoaded) return;
        _gameLoaded = false;
        foreach (var entry in _entries)
            if (entry.RuntimeAvailable) RuntimeUnavailable(entry);
    }

    /// <summary>Withdraws integrations whose dependencies the loader has stopped.</summary>
    internal void OnDependenciesChanged()
    {
        if (_disposed || !_registered) return;
        foreach (var entry in _entries)
            if (entry.State == IntegrationState.Active) CheckRunning(entry);
    }

    /// <summary>Applies a changed settings switch: registers newly enabled plugins and removes disabled ones.</summary>
    internal void OnSettingsApplied()
    {
        if (_disposed || !_registered) return;
        OnDependenciesChanged();
        foreach (var entry in _entries)
        {
            if (entry.Switch == null) continue;
            var on = ReadSwitch(entry);
            if (on && entry.State is IntegrationState.Disabled or IntegrationState.NotRunning)
            {
                TryRegister(entry);
                if (entry.State == IntegrationState.Active && _gameLoaded) RuntimeAvailable(entry);
            }
            else if (!on && entry.State == IntegrationState.Active)
            {
                Deactivate(entry, IntegrationState.Disabled);
                if (entry.State == IntegrationState.Disabled) Report(entry, "disabled_in_settings");
            }
        }
        _decorations?.OnSettingsApplied();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries)
        {
            if (entry.State == IntegrationState.Active)
            {
                Deactivate(entry, IntegrationState.Disabled);
                if (entry.State == IntegrationState.Disabled) Report(entry, "host_stopped");
            }
            else if (entry.Applied.Count > 0) RemovePatches(entry);
        }
    }

    private void RegisterAll()
    {
        if (_disposed || _registered) return;
        _registered = true;
        foreach (var entry in _entries)
        {
            if (entry.State != IntegrationState.Disabled) continue;
            // The loader's enabled list is the saved mod order, which keeps a mod after it was uninstalled. Every mod
            // folder is listed once all mods are loaded, so a mod missing there gets no settings, as if not enabled.
            if (!_environment.IsModInstalled(entry.ModId))
            {
                entry.State = IntegrationState.Absent;
                continue;
            }

            try
            {
                entry.Version = _environment.ModVersion(entry.ModId) ?? string.Empty;
                var verifiedVersions = entry.Plugin.VerifiedVersions;
                entry.Verified = verifiedVersions?.Contains(entry.Version) == true;
                var policy = entry.Plugin as IIntegrationVersionPolicy;
                entry.HasVersionPolicy = policy != null;
                entry.Supported = policy?.SupportsVersion(entry.Version) ?? true;
                entry.DefaultEnabled = (policy == null && entry.Verified) ||
                    (entry.Supported && _environment.IsModRunning(entry.ModId) &&
                        ((policy != null && verifiedVersions?.Count > 0) ||
                            (entry.Plugin is IntegrationPlugin plugin && plugin.CanEnableByDefault())));
                var sharedSwitch = policy != null ? entry.Supported : entry.Verified;
                var (enabled, config, setUiState) = _settings.Define(entry.Plugin, entry.Version, sharedSwitch,
                    entry.DefaultEnabled, () => StatusOf(entry));
                entry.Switch = enabled;
                entry.SetUiState = setUiState;
                entry.Plugin.RegisterSettings(config);
            }
            catch (Exception error)
            {
                Fault(entry, "settings", error);
                continue;
            }

            if (!entry.Supported)
            {
                entry.State = IntegrationState.Incompatible;
                Report(entry, "unsupported_version", IntegrationSeverity.Warning);
                continue;
            }

            if (entry.HasVersionPolicy && !CheckRunning(entry)) continue;
            if (!ReadSwitch(entry))
            {
                Report(entry, entry.HasVersionPolicy || entry.DefaultEnabled ? "disabled_in_settings" : "unverified_version");
                continue;
            }

            TryRegister(entry);
        }
    }

    private bool CheckRunning(Entry entry)
    {
        if (!_environment.IsModRunning(entry.ModId))
        {
            if (entry.State == IntegrationState.Active) Deactivate(entry, IntegrationState.NotRunning);
            else entry.State = IntegrationState.NotRunning;
            Report(entry, "dependency_not_running", IntegrationSeverity.Warning);
            return false;
        }
        return true;
    }

    private void TryRegister(Entry entry)
    {
        if (!entry.Supported || !CheckRunning(entry)) return;

        var registration = new IntegrationRegistration(entry.Version, _runtime, entry.Uid ?? string.Empty);
        if (!Call(entry, "register", () => entry.Plugin.Register(registration))) return;
        if (registration.Failure != null)
        {
            entry.State = IntegrationState.Incompatible;
            var reason = registration.Failure switch
            {
                "a patch target was not found" => "patch_target_missing",
                _ when entry.HasVersionPolicy && IsReasonCode(registration.Failure) => registration.Failure,
                _ => "registration_rejected"
            };
            Report(entry, reason, IntegrationSeverity.Warning);
            return;
        }

        var decorations = registration.Decorations.ToArray();
        foreach (var decoration in decorations)
        {
            var available = false;
            if (!Call(entry, "decoration_overlay", () => available = _overlayAvailable?.Invoke(decoration.OverlayKey) == true)) return;
            if (available) continue;
            entry.State = IntegrationState.Incompatible;
            Report(entry, "decoration_overlay_unavailable", IntegrationSeverity.Warning, decorationId: decoration.Id);
            return;
        }

        foreach (var patch in registration.Patches)
        {
            try
            {
                _patcher.Apply(patch);
                entry.Applied.Add(patch);
            }
            catch (Exception error)
            {
                entry.Applied.Add(patch);
                if (RemovePatches(entry))
                {
                    entry.State = IntegrationState.Faulted;
                    Report(entry, "patch_apply_failed", IntegrationSeverity.Error, error);
                }
                return;
            }
        }

        foreach (var check in registration.Checks)
        {
            string? problem;
            try
            {
                problem = check();
            }
            catch (Exception error)
            {
                Fault(entry, "patch_check", error);
                return;
            }

            if (problem == null) continue;
            if (RemovePatches(entry))
            {
                entry.State = IntegrationState.Incompatible;
                Report(entry, "patch_check_rejected", IntegrationSeverity.Warning);
            }
            return;
        }

        entry.Registration = registration;
        entry.Decorations = decorations;
        entry.State = IntegrationState.Active;
        Report(entry, entry.Verified ? "enabled" : "enabled_unverified");
    }

    private static bool IsReasonCode(string value) => value.Length is > 0 and <= 64 &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private void RuntimeAvailable(Entry entry)
    {
        var registration = entry.Registration!;
        var pluginId = entry.Uid!;
        if (_decorations != null && entry.Decorations.Count > 0)
            registration.BeginDecorationScope(() => _decorations.Invalidate(pluginId));
        if (!Call(entry, "runtime_attach", () => entry.Plugin.OnRuntimeAvailable(_runtime))) return;
        entry.RuntimeAvailable = true;
        _decorations?.Activate(pluginId, entry.Decorations);
    }

    private void RuntimeUnavailable(Entry entry)
    {
        entry.RuntimeAvailable = false;
        entry.Registration?.EndDecorationScope();
        _decorations?.Withdraw(entry.Uid ?? string.Empty);
        Call(entry, "runtime_detach", entry.Plugin.OnRuntimeUnavailable);
    }

    private void Deactivate(Entry entry, IntegrationState state)
    {
        if (entry.RuntimeAvailable) RuntimeUnavailable(entry);
        if (entry.State == IntegrationState.Active) entry.State = state;
        entry.Registration = null;
        entry.Decorations = Array.Empty<IntegrationDecorationCatalog.Contribution>();
        RemovePatches(entry);
    }

    private bool Call(Entry entry, string phase, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception error)
        {
            Fault(entry, phase, error);
            return false;
        }
    }

    private void Fault(Entry entry, string phase, Exception error) =>
        Fail(entry, phase + "_failed", error.GetType().Name);

    private void Fail(Entry entry, string reason, string? exceptionType, string? decorationId = null)
    {
        entry.Registration?.EndDecorationScope();
        entry.Registration = null;
        entry.Decorations = Array.Empty<IntegrationDecorationCatalog.Contribution>();
        _decorations?.Withdraw(entry.Uid ?? string.Empty);
        if (entry.RuntimeAvailable)
        {
            entry.RuntimeAvailable = false;
            try
            {
                entry.Plugin.OnRuntimeUnavailable();
            }
            catch (Exception error)
            {
                Emit(entry, "runtime_detach_failed", IntegrationSeverity.Error, error);
            }
        }

        entry.State = IntegrationState.Faulted;
        Report(entry, reason, IntegrationSeverity.Error, exceptionType: exceptionType, decorationId: decorationId);
        RemovePatches(entry);
    }

    // A failed removal stays recorded so Dispose can retry it.
    private bool RemovePatches(Entry entry)
    {
        Exception? failure = null;
        for (var index = entry.Applied.Count - 1; index >= 0; index--)
        {
            try
            {
                _patcher.Remove(entry.Applied[index]);
                entry.Applied.RemoveAt(index);
            }
            catch (Exception error)
            {
                failure ??= error;
            }
        }

        if (failure == null) return true;
        entry.RuntimeAvailable = false;
        entry.State = IntegrationState.Faulted;
        Report(entry, "patch_remove_failed", IntegrationSeverity.Error, failure);
        return false;
    }

    private void FaultDecoration(string pluginId, string decorationId, string reason, string? errorType)
    {
        var entry = _entries.FirstOrDefault(value => value.Uid == pluginId);
        if (entry == null || entry.State != IntegrationState.Active) return;
        Fail(entry, reason, errorType, decorationId);
    }

    private bool ReadSwitch(Entry entry)
    {
        try
        {
            return entry.Switch?.Invoke() == true;
        }
        catch (Exception error)
        {
            Emit(entry, "setting_read_failed", IntegrationSeverity.Error, error);
            return false;
        }
    }

    private static string? StatusOf(Entry entry) => entry.State switch
    {
        IntegrationState.Incompatible => StatusIncompatible,
        IntegrationState.Faulted => StatusFaulted,
        IntegrationState.NotRunning => StatusNotRunning,
        IntegrationState.Active when entry.HasVersionPolicy => entry.Verified ? StatusActive : StatusActiveUnverified,
        IntegrationState.Disabled when entry.HasVersionPolicy => StatusDisabled,
        _ => entry.DefaultEnabled ? null : StatusUnverified
    };

    private void Report(Entry entry, string reason, IntegrationSeverity severity = IntegrationSeverity.Info,
        Exception? error = null, string? exceptionType = null, string? decorationId = null)
    {
        try
        {
            var locked = entry.State is IntegrationState.NotRunning or IntegrationState.Incompatible or IntegrationState.Faulted;
            var labelState = entry.State == IntegrationState.NotRunning ? StatusNotRunningLabel
                : entry.State is IntegrationState.Incompatible or IntegrationState.Faulted ? StatusFailedLabel : null;
            entry.SetUiState?.Invoke(locked, labelState);
        }
        catch (Exception statusError)
        {
            Emit(entry, "setting_status_failed", IntegrationSeverity.Error, statusError);
        }

        var status = (entry.State, reason);
        if (entry.LastStatus == status) return;
        entry.LastStatus = status;
        _log(new IntegrationNotice(severity, entry.Uid ?? "unknown", entry.ModId, entry.Version,
            entry.State, entry.Verified, reason, exceptionType ?? error?.GetType().Name, decorationId));
    }

    private void Emit(Entry entry, string reason, IntegrationSeverity severity, Exception? error) =>
        _log(new IntegrationNotice(severity, entry.Uid ?? "unknown", entry.ModId, entry.Version,
            entry.State, entry.Verified, reason, error?.GetType().Name));

    private sealed class Entry
    {
        internal Entry(IIntegrationPlugin plugin) => Plugin = plugin;

        internal IIntegrationPlugin Plugin { get; }

        internal string? Uid { get; set; }

        internal string ModId { get; set; } = string.Empty;

        internal string Version { get; set; } = string.Empty;

        internal bool Verified { get; set; }

        internal bool HasVersionPolicy { get; set; }

        internal bool Supported { get; set; }

        internal bool DefaultEnabled { get; set; }

        internal Func<bool>? Switch { get; set; }

        internal Action<bool, string?>? SetUiState { get; set; }

        internal IntegrationState State { get; set; } = IntegrationState.Absent;

        internal (IntegrationState State, string Reason)? LastStatus { get; set; }

        internal bool RuntimeAvailable { get; set; }

        internal List<PatchRequest> Applied { get; } = new();
        internal IntegrationRegistration? Registration { get; set; }
        internal IReadOnlyList<IntegrationDecorationCatalog.Contribution> Decorations { get; set; } =
            Array.Empty<IntegrationDecorationCatalog.Contribution>();
    }
}
