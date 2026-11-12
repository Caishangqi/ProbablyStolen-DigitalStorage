using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Integration;
using ProbablyStolen.DigitalStorage.Api.Networking;
using ProbablyStolen.DigitalStorage.Api.Storage;
using ProbablyStolen.DigitalStorage.Platform.Integration;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class IntegrationHostTests
{
    private const string Uid = "digital_storage:other_mod";
    private static readonly MethodBase Target = typeof(PatchTargets).GetMethod(nameof(PatchTargets.Run))!;
    private static readonly MethodInfo Prefix = typeof(PatchTargets).GetMethod(nameof(PatchTargets.Prefix))!;

    private FakeEnvironment _environment = null!;
    private FakePatcher _patcher = null!;
    private FakeSettings _settings = null!;
    private List<string> _log = null!;

    [SetUp]
    public void SetUp()
    {
        _environment = new FakeEnvironment();
        _patcher = new FakePatcher();
        _settings = new FakeSettings();
        _log = new List<string>();
    }

    private IntegrationHost Host(params IIntegrationPlugin[] plugins) =>
        new(plugins, _environment, _patcher, _settings, new FakeRuntime(), _log.Add);

    private IntegrationHost Started(FakePlugin plugin)
    {
        var host = Host(plugin);
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        return host;
    }

    [Test]
    public void AbsentMod_IsNeverCalledAndNotLogged()
    {
        _environment.Enabled.Clear();
        var plugin = new FakePlugin();

        var host = Started(plugin);
        host.OnGameLoaded();
        host.Dispose();

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Absent));
        Assert.That(plugin.Calls, Is.Empty);
        Assert.That(_settings.Defined, Is.Empty, "No switch appears when the other mod is not installed.");
        Assert.That(_log, Is.Empty);
    }

    [Test]
    public void VerifiedVersion_DefaultsOn_RegistersOnceModsAreLoadedAndPatches()
    {
        var plugin = new FakePlugin { Patch = (r) => r.Patch(Target, Prefix) };
        var host = Host(plugin);

        host.Initialize();
        host.Enable();
        Assert.That(plugin.Calls, Is.Empty, "Registering waits until every mod is loaded.");
        Assert.That(_settings.Defined, Is.Empty, "The other mod's version is only known once every mod is loaded.");

        host.OnModsLoaded();
        host.OnModsLoaded();

        Assert.That(_settings.Defaults[Uid], Is.True);
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Active));
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings", "register" }));
        Assert.That(_patcher.Applied.Select(p => p.Target), Is.EqualTo(new[] { Target }));
        Assert.That(_log, Has.One.Contains("Active"));
    }

    [Test]
    public void UnverifiedVersion_DefaultsOff_AndCanBeTurnedOn()
    {
        _environment.Versions["other_mod"] = "9.9.9";
        var plugin = new FakePlugin { Patch = (r) => r.Patch(Target, Prefix) };
        var host = Started(plugin);

        Assert.That(_settings.Defaults[Uid], Is.False);
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Disabled));
        Assert.That(_settings.Status[Uid](), Is.EqualTo(IntegrationHost.StatusUnverified));
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings" }), "Settings exist; nothing registers.");

        _settings.Values[Uid] = true;
        host.OnSettingsApplied();

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Active));
        Assert.That(_log, Has.Some.Contains("unverified"));
    }

    [Test]
    public void ModNotRunning_IsReportedAndNotRegistered()
    {
        _environment.Running.Clear();
        var plugin = new FakePlugin();

        var host = Started(plugin);

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.NotRunning));
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings" }), "Settings exist; the other mod is not running.");
        Assert.That(_settings.Status[Uid](), Is.EqualTo(IntegrationHost.StatusNotRunning));
    }

    [Test]
    public void MissingTarget_MarksIncompatible_AndAppliesNothing()
    {
        var plugin = new FakePlugin
        {
            Patch = (r) =>
            {
                r.Patch(Target, Prefix);
                r.Patch(null, Prefix);
            }
        };

        var host = Started(plugin);

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Incompatible));
        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(_settings.Status[Uid](), Is.EqualTo(IntegrationHost.StatusIncompatible));
    }

    [Test]
    public void FailingPatch_RemovesThePatchesAlreadyApplied()
    {
        _patcher.FailOnCall = 2;
        var plugin = new FakePlugin
        {
            Patch = (r) =>
            {
                r.Patch(Target, Prefix);
                r.Patch(Target, postfix: Prefix);
            }
        };

        var host = Started(plugin);

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Incompatible));
        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(_patcher.Removed, Has.Count.EqualTo(1));
    }

    [Test]
    public void FailingCheck_RemovesPatches()
    {
        var plugin = new FakePlugin
        {
            Patch = (r) =>
            {
                r.Patch(Target, Prefix);
                r.Verify(() => "the call to rewrite was not found");
            }
        };

        var host = Started(plugin);

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Incompatible));
        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(_log, Has.Some.Contains("not found"));
    }

    [Test]
    public void ThrowingPlugin_IsFaulted_AndOtherPluginsStillRun()
    {
        var broken = new FakePlugin { Uid = "digital_storage:broken", Patch = _ => throw new InvalidOperationException("boom") };
        var healthy = new FakePlugin { Patch = (r) => r.Patch(Target, Prefix) };
        var host = Host(broken, healthy);

        host.Initialize();
        host.Enable();
        host.OnModsLoaded();

        Assert.That(host.StateOf("digital_storage:broken"), Is.EqualTo(IntegrationState.Faulted));
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Active));
        Assert.That(_log, Has.Some.Contains("boom"));
    }

    [Test]
    public void RuntimePhases_FollowGameLoads_AndDisposeRemovesPatches()
    {
        var plugin = new FakePlugin { Patch = (r) => r.Patch(Target, Prefix) };
        var host = Started(plugin);

        host.OnGameLoaded();
        host.OnGameLoaded();
        host.Dispose();

        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings", "register", "available", "unavailable", "available", "unavailable" }));
        Assert.That(_patcher.Applied, Is.Empty);
    }

    [Test]
    public void SwitchingOff_UnloadsThePlugin_AndOnAgainRegisters()
    {
        var plugin = new FakePlugin { Patch = (r) => r.Patch(Target, Prefix) };
        var host = Started(plugin);
        host.OnGameLoaded();

        _settings.Values[Uid] = false;
        host.OnSettingsApplied();
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Disabled));
        Assert.That(_patcher.Applied, Is.Empty);

        _settings.Values[Uid] = true;
        host.OnSettingsApplied();
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Active));
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings", "register", "available", "unavailable", "register", "available" }));
    }

    [Test]
    public void UnremovablePatch_FaultsThePlugin_AndDisposeTriesAgain()
    {
        var plugin = new FakePlugin { Patch = (r) => r.Patch(Target, Prefix) };
        var host = Started(plugin);
        _patcher.FailRemove = true;

        _settings.Values[Uid] = false;
        host.OnSettingsApplied();
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Faulted));
        Assert.That(_patcher.Applied, Has.Count.EqualTo(1));

        _patcher.FailRemove = false;
        host.Dispose();
        Assert.That(_patcher.Applied, Is.Empty);
    }

    [Test]
    public void PluginOptions_FollowTheSwitch_EvenWhenItIsOff()
    {
        _environment.Versions["other_mod"] = "9.9.9";
        var plugin = new FakePlugin { Settings = config => config.DefineBool("fast", true, "label", "tooltip") };

        var host = Started(plugin);

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Disabled));
        Assert.That(_settings.Options, Is.EqualTo(new[] { Uid + "/fast" }));
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings" }), "Options exist before the plugin is switched on.");
    }

    [Test]
    public void ThrowingSettings_FaultOnlyThatPlugin()
    {
        var plugin = new FakePlugin { Settings = _ => throw new InvalidOperationException("bad option") };

        var host = Started(plugin);

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Faulted));
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings" }), "A faulted plugin is never registered.");
    }

    [Test]
    public void StateLines_AreLoggedOncePerState()
    {
        _environment.Running.Clear();
        var host = Started(new FakePlugin());

        host.OnSettingsApplied();
        host.OnSettingsApplied();

        Assert.That(_log.Count(line => line.Contains("NotRunning")), Is.EqualTo(1));
    }

    [Test]
    public void Discover_CreatesMarkedPlugins_AndSkipsBrokenOnes()
    {
        var log = new List<string>();

        var plugins = IntegrationHost.Discover(new[] { typeof(MarkedPlugin), typeof(UnmarkedPlugin), typeof(MarkedNotAPlugin) }, log.Add);

        Assert.That(plugins.Select(plugin => plugin.GetType()), Is.EqualTo(new[] { typeof(MarkedPlugin) }));
        Assert.That(log, Has.One.Contains(nameof(MarkedNotAPlugin)));
    }

    public static class PatchTargets
    {
        public static void Run()
        {
        }

        public static void Prefix()
        {
        }
    }

    [IntegrationPlugin]
    public sealed class MarkedPlugin : FakePlugin
    {
    }

    public sealed class UnmarkedPlugin : FakePlugin
    {
    }

    [IntegrationPlugin]
    public sealed class MarkedNotAPlugin
    {
    }

    public class FakePlugin : IntegrationPlugin
    {
        internal string Uid { get; set; } = IntegrationHostTests.Uid;

        internal Action<IIntegrationRegistration>? Patch { get; set; }

        internal Action<IIntegrationConfig>? Settings { get; set; }

        internal List<string> Calls { get; } = new();

        public override string PluginUid => Uid;

        public override string RequiredModId => "other_mod";

        public override IReadOnlyCollection<string> VerifiedVersions => new[] { "1.0.0" };

        public override string SettingSectionKey => "integration_other_section";

        public override string SettingLabelKey => "integration_other_label";

        public override string SettingTooltipKey => "integration_other_tooltip";

        public override void RegisterSettings(IIntegrationConfig config)
        {
            Calls.Add("settings");
            Settings?.Invoke(config);
        }

        public override void Register(IIntegrationRegistration registration)
        {
            Calls.Add("register");
            Patch?.Invoke(registration);
        }

        public override void OnRuntimeAvailable(IDigitalStorageRuntime runtime) => Calls.Add("available");

        public override void OnRuntimeUnavailable() => Calls.Add("unavailable");
    }

    private sealed class FakeEnvironment : IIntegrationEnvironment
    {
        internal HashSet<string> Enabled { get; } = new() { "other_mod" };

        internal HashSet<string> Running { get; } = new() { "other_mod" };

        internal Dictionary<string, string> Versions { get; } = new() { ["other_mod"] = "1.0.0" };

        public bool IsModEnabled(string modId) => Enabled.Contains(modId);

        public bool IsModRunning(string modId) => Running.Contains(modId);

        public string? ModVersion(string modId) => Versions.TryGetValue(modId, out var version) ? version : null;
    }

    private sealed class FakePatcher : IIntegrationPatcher
    {
        private int _calls;

        internal int FailOnCall { get; set; } = -1;

        internal List<PatchRequest> Applied { get; } = new();

        internal List<PatchRequest> Removed { get; } = new();

        public void Apply(PatchRequest request)
        {
            if (++_calls == FailOnCall) throw new InvalidOperationException("Harmony refused");
            Applied.Add(request);
        }

        internal bool FailRemove { get; set; }

        public void Remove(PatchRequest request)
        {
            if (FailRemove) throw new InvalidOperationException("Harmony could not unpatch");
            Applied.Remove(request);
            Removed.Add(request);
        }
    }

    private sealed class FakeSettings : IIntegrationSettings
    {
        internal Dictionary<string, bool> Defaults { get; } = new();

        internal Dictionary<string, bool> Values { get; } = new();

        internal Dictionary<string, Func<string?>> Status { get; } = new();

        internal List<string> Defined { get; } = new();

        internal List<string> Options { get; } = new();

        public (Func<bool> Switch, IIntegrationConfig Config) Define(IIntegrationPlugin plugin, string modVersion,
            bool verified, Func<string?> status)
        {
            var defaultValue = verified;
            var uid = plugin.PluginUid;
            Defined.Add(uid);
            Defaults[uid] = defaultValue;
            Values[uid] = defaultValue;
            Status[uid] = status;
            return (() => Values[uid], new FakeConfig(this, uid));
        }

        private sealed class FakeConfig : IIntegrationConfig
        {
            private readonly FakeSettings _owner;
            private readonly string _uid;

            internal FakeConfig(FakeSettings owner, string uid)
            {
                _owner = owner;
                _uid = uid;
            }

            public IIntegrationOption<bool> DefineBool(string key, bool defaultValue, string labelKey, string tooltipKey)
            {
                _owner.Options.Add($"{_uid}/{key}");
                return new FakeOption<bool>(defaultValue);
            }

            public IIntegrationOption<int> DefineInt(string key, int defaultValue, int min, int max, string labelKey,
                string tooltipKey)
            {
                _owner.Options.Add($"{_uid}/{key}");
                return new FakeOption<int>(defaultValue);
            }
        }

        private sealed class FakeOption<T> : IIntegrationOption<T>
        {
            internal FakeOption(T value) => Value = value;

            public T Value { get; }
        }
    }

    private sealed class FakeRuntime : IDigitalStorageRuntime
    {
        public IGridHelper Grids => throw new NotSupportedException();

        public IExternalAccess ExternalAccess => throw new NotSupportedException();
    }
}
