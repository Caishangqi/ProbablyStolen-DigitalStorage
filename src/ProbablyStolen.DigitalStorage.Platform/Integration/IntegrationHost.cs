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
    internal const string StatusUnverified = "integration_state_unverified";

    private readonly List<Entry> _entries;
    private readonly IIntegrationEnvironment _environment;
    private readonly IIntegrationPatcher _patcher;
    private readonly IIntegrationSettings _settings;
    private readonly IDigitalStorageRuntime _runtime;
    private readonly Action<string> _log;
    private readonly HashSet<(string, IntegrationState)> _logged = new();
    private bool _initialized;
    private bool _enabled;
    private bool _registered;
    private bool _gameLoaded;
    private bool _disposed;

    internal IntegrationHost(IEnumerable<IIntegrationPlugin> plugins, IIntegrationEnvironment environment,
        IIntegrationPatcher patcher, IIntegrationSettings settings, IDigitalStorageRuntime runtime, Action<string> log)
    {
        _entries = (plugins ?? throw new ArgumentNullException(nameof(plugins))).Select(plugin => new Entry(plugin)).ToList();
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _patcher = patcher ?? throw new ArgumentNullException(nameof(patcher));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Creates plugins among <paramref name="types"/> that carry <see cref="IntegrationPluginAttribute"/>; a type
    /// that cannot be created is logged and skipped.
    /// </summary>
    /// <remarks>
    /// The caller must pass <c>typeof(T).Assembly.GetTypes()</c> of the plugin assembly literally so the mod scanner
    /// recognizes reflection on the mod's own types.
    /// </remarks>
    internal static IReadOnlyList<IIntegrationPlugin> Discover(IEnumerable<Type> types, Action<string> log)
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
                log($"Digital Storage: integration plugin {type.FullName} was skipped ({Describe(error)}).");
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

    /// <summary>Runs the runtime phases for a newly loaded game.</summary>
    internal void OnGameLoaded()
    {
        if (_disposed) return;
        foreach (var entry in _entries.Where(entry => entry.State == IntegrationState.Active))
        {
            if (entry.RuntimeAvailable) RuntimeUnavailable(entry);
            RuntimeAvailable(entry);
        }

        _gameLoaded = true;
    }

    /// <summary>Applies a changed settings switch: registers newly enabled plugins and removes disabled ones.</summary>
    internal void OnSettingsApplied()
    {
        if (_disposed || !_registered) return;
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
                Report(entry, "disabled in the settings");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries)
        {
            if (entry.State == IntegrationState.Active) Deactivate(entry, IntegrationState.Disabled);
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
            try
            {
                entry.Version = _environment.ModVersion(entry.ModId) ?? string.Empty;
                entry.Verified = entry.Plugin.VerifiedVersions?.Contains(entry.Version) == true;
                var (enabled, config) = _settings.Define(entry.Plugin, entry.Version, entry.Verified, () => StatusOf(entry));
                entry.Switch = enabled;
                entry.Plugin.RegisterSettings(config);
            }
            catch (Exception error)
            {
                Fault(entry, "settings", error);
                continue;
            }

            if (!ReadSwitch(entry))
            {
                Report(entry, entry.Verified ? "disabled in the settings" : "disabled: version not verified");
                continue;
            }

            TryRegister(entry);
        }
    }

    private void TryRegister(Entry entry)
    {
        if (!_environment.IsModRunning(entry.ModId))
        {
            entry.State = IntegrationState.NotRunning;
            Report(entry, "the required mod is not running");
            return;
        }

        var registration = new IntegrationRegistration(entry.Version, _runtime);
        if (!Call(entry, "register", () => entry.Plugin.Register(registration))) return;
        if (registration.Failure != null)
        {
            entry.State = IntegrationState.Incompatible;
            entry.Reason = registration.Failure;
            Report(entry, registration.Failure);
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
                RemovePatches(entry);
                entry.State = IntegrationState.Incompatible;
                entry.Reason = $"patching {patch.Target.Name} failed ({Describe(error)})";
                Report(entry, entry.Reason);
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
                problem = $"a check threw ({Describe(error)})";
            }

            if (problem == null) continue;
            RemovePatches(entry);
            entry.State = IntegrationState.Incompatible;
            entry.Reason = problem;
            Report(entry, problem);
            return;
        }

        entry.State = IntegrationState.Active;
        entry.Reason = null;
        Report(entry, entry.Verified ? "active" : "active on an unverified version");
    }

    private void RuntimeAvailable(Entry entry)
    {
        if (Call(entry, "runtime available", () => entry.Plugin.OnRuntimeAvailable(_runtime))) entry.RuntimeAvailable = true;
    }

    private void RuntimeUnavailable(Entry entry)
    {
        entry.RuntimeAvailable = false;
        Call(entry, "runtime unavailable", entry.Plugin.OnRuntimeUnavailable);
    }

    private void Deactivate(Entry entry, IntegrationState state)
    {
        if (entry.RuntimeAvailable) RuntimeUnavailable(entry);
        if (entry.State == IntegrationState.Active) entry.State = state;
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

    private void Fault(Entry entry, string phase, Exception error)
    {
        entry.RuntimeAvailable = false;
        RemovePatches(entry);
        entry.State = IntegrationState.Faulted;
        entry.Reason = $"{phase} failed ({Describe(error)})";
        Report(entry, entry.Reason);
    }

    // Patches whose removal failed stay recorded, so the plugin reports Faulted and Dispose tries again.
    private void RemovePatches(Entry entry)
    {
        for (var index = entry.Applied.Count - 1; index >= 0; index--)
        {
            try
            {
                _patcher.Remove(entry.Applied[index]);
                entry.Applied.RemoveAt(index);
            }
            catch (Exception error)
            {
                _log($"Digital Storage: integration {entry.Uid} could not remove a patch of " +
                    $"{entry.Applied[index].Target.Name} ({Describe(error)}).");
            }
        }

        if (entry.Applied.Count == 0) return;
        entry.RuntimeAvailable = false;
        entry.State = IntegrationState.Faulted;
        entry.Reason = "a patch could not be removed";
        Report(entry, entry.Reason);
    }

    private bool ReadSwitch(Entry entry)
    {
        try
        {
            return entry.Switch?.Invoke() == true;
        }
        catch (Exception error)
        {
            _log($"Digital Storage: integration {entry.Uid} setting could not be read ({Describe(error)}).");
            return false;
        }
    }

    private static string? StatusOf(Entry entry) => entry.State switch
    {
        IntegrationState.Incompatible or IntegrationState.Faulted => StatusIncompatible,
        IntegrationState.NotRunning => StatusNotRunning,
        _ => entry.Verified ? null : StatusUnverified
    };

    private void Report(Entry entry, string message)
    {
        if (!_logged.Add((entry.Uid ?? "?", entry.State))) return;
        var version = string.IsNullOrEmpty(entry.Version) ? "unknown version" : entry.Version;
        _log($"Digital Storage: integration {entry.Uid} for {entry.ModId} {version}: {entry.State} ({message}).");
    }

    private static string Describe(Exception error) => $"{error.GetType().Name}: {error.Message}";

    private sealed class Entry
    {
        internal Entry(IIntegrationPlugin plugin) => Plugin = plugin;

        internal IIntegrationPlugin Plugin { get; }

        internal string? Uid { get; set; }

        internal string ModId { get; set; } = string.Empty;

        internal string Version { get; set; } = string.Empty;

        internal bool Verified { get; set; }

        internal Func<bool>? Switch { get; set; }

        internal IntegrationState State { get; set; } = IntegrationState.Absent;

        internal string? Reason { get; set; }

        internal bool RuntimeAvailable { get; set; }

        internal List<PatchRequest> Applied { get; } = new();
    }
}
