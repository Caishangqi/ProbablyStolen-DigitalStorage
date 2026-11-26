using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Integration;
using ProbablyStolen.DigitalStorage.Api.Networking;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Api.Storage;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Compat.BrewingExpansion;
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
    private List<IntegrationNotice> _log = null!;

    [SetUp]
    public void SetUp()
    {
        _environment = new FakeEnvironment();
        _patcher = new FakePatcher();
        _settings = new FakeSettings();
        _log = new List<IntegrationNotice>();
    }

    private IntegrationHost Host(params IIntegrationPlugin[] plugins) =>
        new(plugins, _environment, _patcher, _settings, new FakeRuntime(), _log.Add);

    private IntegrationHost HostWithDecorations(IntegrationDecorationCatalog catalog, params IIntegrationPlugin[] plugins) =>
        new(plugins, _environment, _patcher, _settings, new FakeRuntime(), _log.Add, catalog, _ => true);

    private IntegrationHost Started(IIntegrationPlugin plugin)
    {
        var host = Host(plugin);
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        return host;
    }

    [Test]
    public void FailedIntegrationSwitchLocksOnlyItsEntryAndClearRemovesTheLock()
    {
        const string modId = "digital_storage";
        const string firstKey = "integration_first_enabled_untested_1.0.0";
        const string secondKey = "integration_second_enabled_untested_1.0.0";
        var changes = new List<(string ModId, string Key, bool Locked, string? LabelState)>();
        void OnChanged(string changedMod, string key, bool locked, string? labelState) =>
            changes.Add((changedMod, key, locked, labelState));

        IntegrationSettingsView.SwitchStateChanged += OnChanged;
        IntegrationSettingsView.SetSwitchState(modId, firstKey, true, IntegrationHost.StatusFailedLabel);
        try
        {
            Assert.That(IntegrationSettingsView.IsIntegrationSwitch(modId, firstKey), Is.True);
            Assert.That(IntegrationSettingsView.IsSwitchLocked(modId, firstKey), Is.True);
            Assert.That(IntegrationSettingsView.IsSwitchLocked(modId, secondKey), Is.False);
            Assert.That(IntegrationSettingsView.IsSwitchLocked("other_mod", firstKey), Is.False);
            Assert.That(changes, Is.EqualTo(new[] { (modId, firstKey, true, IntegrationHost.StatusFailedLabel) }));
        }
        finally
        {
            IntegrationSettingsView.ClearMod(modId);
            IntegrationSettingsView.SwitchStateChanged -= OnChanged;
        }

        Assert.That(IntegrationSettingsView.IsSwitchLocked(modId, firstKey), Is.False);
        Assert.That(changes, Has.Count.EqualTo(2));
        Assert.That(changes[1].Locked, Is.False);
        Assert.That(changes[1].LabelState, Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DecorationCatalog_WithdrawsStoppedDependencyWithoutSettingsApply(bool loaderNotified)
    {
        var catalog = new IntegrationDecorationCatalog();
        var plugin = new FakePlugin
        {
            Patch = registration => ((ITerminalDecorationRegistration)registration).AddSlotDecoration(
                "marker", "demand_border", _ => new int?[] { 0x654321 })
        };
        var host = HostWithDecorations(catalog, plugin);
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        var samples = new GameItem[] { null! };

        Assert.That(catalog.Evaluate(samples)[0], Is.Empty, "Registration alone does not activate decorations.");
        host.OnGameLoaded();
        Assert.That(catalog.Evaluate(samples)[0].Single().TintRgb, Is.EqualTo(0x654321));

        _settings.Values[Uid] = false;
        host.OnSettingsApplied();
        Assert.That(catalog.Evaluate(samples)[0], Is.Empty);
        Assert.That(plugin.Calls, Does.Contain("unavailable"));

        _settings.Values[Uid] = true;
        host.OnSettingsApplied();
        Assert.That(catalog.Evaluate(samples)[0].Single().TintRgb, Is.EqualTo(0x654321));
        _environment.Running.Remove("other_mod");
        if (loaderNotified) host.OnDependenciesChanged();
        var screen = new StorageScreen(1, 1);
        screen.SetEntriesInOrder(new[] { new TerminalEntry(new ItemKey("apple"), 1, "Apple", "sprite") });
        var sampleLookups = 0;
        var external = catalog.EvaluatePage(screen.GetPage(), true, _ =>
        {
            sampleLookups++;
            return null;
        });
        Assert.That(external, Is.Null);
        Assert.That(sampleLookups, Is.Zero);
        Assert.That(catalog.Evaluate(samples)[0], Is.Empty);
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.NotRunning));
        Assert.That(plugin.Calls.Count(call => call == "unavailable"), Is.EqualTo(2));
        host.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FreshGame_ActivatesDecorationsWithNoPreviousSaveLoad(bool enabledAtStart)
    {
        var catalog = new IntegrationDecorationCatalog();
        _settings.Values[Uid] = enabledAtStart;
        var plugin = new FakePlugin
        {
            Patch = registration => ((ITerminalDecorationRegistration)registration).AddSlotDecoration(
                "marker", "test_border", _ => new int?[] { 0x123456 })
        };
        using var host = HostWithDecorations(catalog, plugin);
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        host.OnNewGameStarted("test_scenario");

        Assert.That(catalog.HasActive, Is.EqualTo(enabledAtStart));
        if (!enabledAtStart)
        {
            _settings.Values[Uid] = true;
            host.OnSettingsApplied();
        }

        Assert.That(catalog.Evaluate(new GameItem[] { null! })[0].Single().TintRgb, Is.EqualTo(0x123456));
    }

    [Test]
    public void InvalidationCallbacks_FollowRuntimeAndRegistrationAndIsolateThrowingSubscribers()
    {
        var notifications = new List<long>();
        var subscriberErrors = new List<string>();
        var catalog = new IntegrationDecorationCatalog(notificationFailed: subscriberErrors.Add);
        catalog.Changed += () => throw new InvalidOperationException();
        catalog.Changed += () => notifications.Add(catalog.Revision);
        ITerminalDecorationRegistration? registration = null;
        var runtimeCallbacks = new List<Action>();
        var plugin = new FakePlugin
        {
            Patch = value =>
            {
                registration = (ITerminalDecorationRegistration)value;
                registration.AddSlotDecoration("marker", "test_border", _ => new int?[] { 1 });
            },
            RuntimeAttach = () => runtimeCallbacks.Add(registration!.InvalidateSlotDecorations)
        };
        using var host = HostWithDecorations(catalog, plugin);
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        var beforeLoad = registration!.InvalidateSlotDecorations;
        beforeLoad();
        Assert.That(catalog.Revision, Is.Zero);

        host.OnNewGameStarted("test_scenario");
        beforeLoad();
        runtimeCallbacks[0]();
        host.OnGameLoaded();
        runtimeCallbacks[0]();
        runtimeCallbacks[1]();
        host.OnSettingsApplied();

        _settings.Values[Uid] = false;
        host.OnSettingsApplied();
        runtimeCallbacks[1]();
        _settings.Values[Uid] = true;
        host.OnSettingsApplied();
        runtimeCallbacks[0]();
        runtimeCallbacks[1]();
        runtimeCallbacks[2]();

        host.Dispose();
        runtimeCallbacks[2]();
        Assert.That(notifications, Is.EqualTo(Enumerable.Range(1, 11).Select(value => (long)value)));
        Assert.That(subscriberErrors, Has.Count.EqualTo(notifications.Count));
        Assert.That(_log.Any(notice => notice.State == IntegrationState.Faulted), Is.False);
    }

    [Test]
    public void SceneUnload_RevokesRuntimeUntilANewGameLoads()
    {
        var catalog = new IntegrationDecorationCatalog();
        ITerminalDecorationRegistration? registration = null;
        var callbacks = new List<Action>();
        var plugin = new FakePlugin
        {
            Patch = value =>
            {
                registration = (ITerminalDecorationRegistration)value;
                registration.Patch(Target, postfix: Prefix);
                registration.AddSlotDecoration("marker", "test_border", _ => new int?[] { 1 });
            },
            RuntimeAttach = () => callbacks.Add(registration!.InvalidateSlotDecorations)
        };
        using var host = HostWithDecorations(catalog, plugin);
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        host.OnGameLoaded();
        host.OnGameUnloaded();
        host.OnGameUnloaded();

        var afterUnload = catalog.Revision;
        callbacks[0]();
        Assert.That(catalog.Revision, Is.EqualTo(afterUnload));
        Assert.That(catalog.HasActive, Is.False);
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Active));
        Assert.That(_patcher.Applied, Has.Count.EqualTo(1));
        Assert.That(plugin.Calls.Count(call => call == "unavailable"), Is.EqualTo(1));

        _settings.Values[Uid] = false;
        host.OnSettingsApplied();
        _settings.Values[Uid] = true;
        host.OnSettingsApplied();
        Assert.That(callbacks, Has.Count.EqualTo(1));
        Assert.That(catalog.HasActive, Is.False);

        host.OnNewGameStarted("test_scenario");
        Assert.That(callbacks, Has.Count.EqualTo(2));
        Assert.That(catalog.HasActive, Is.True);
        var afterReopen = catalog.Revision;
        callbacks[0]();
        callbacks[1]();
        Assert.That(catalog.Revision, Is.EqualTo(afterReopen + 1));
    }

    [Test]
    public void RuntimeAttachFailure_RevokesItsCallbackAndRollsBackPatches()
    {
        var catalog = new IntegrationDecorationCatalog();
        Action? invalidate = null;
        ITerminalDecorationRegistration? registration = null;
        var plugin = new FakePlugin
        {
            Patch = value =>
            {
                registration = (ITerminalDecorationRegistration)value;
                registration.Patch(Target, postfix: Prefix);
                registration.AddSlotDecoration("marker", "test_border", _ => new int?[] { 1 });
            },
            RuntimeAttach = () =>
            {
                invalidate = registration!.InvalidateSlotDecorations;
                throw new InvalidOperationException();
            }
        };
        using var host = HostWithDecorations(catalog, plugin);
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        host.OnGameLoaded();
        invalidate!();

        Assert.That(catalog.Revision, Is.Zero);
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Faulted));
        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(_log.Last().Reason, Is.EqualTo("runtime_attach_failed"));
    }

    [Test]
    public void MissingOverlay_RejectsAllOfOwningPluginsRegistrationBeforeApplyingPatches()
    {
        var catalog = new IntegrationDecorationCatalog();
        var broken = new FakePlugin
        {
            Uid = "digital_storage:broken",
            Patch = registration =>
            {
                registration.Patch(Target, Prefix);
                var decorations = (ITerminalDecorationRegistration)registration;
                decorations.AddSlotDecoration("valid", "test_border", _ => new int?[] { 1 });
                decorations.AddSlotDecoration("missing", "missing_border", _ => new int?[] { 2 });
            }
        };
        var healthy = new FakePlugin
        {
            Uid = "digital_storage:healthy",
            Patch = registration => ((ITerminalDecorationRegistration)registration).AddSlotDecoration(
                "valid", "test_border", _ => new int?[] { 3 })
        };
        using var host = new IntegrationHost(new[] { broken, healthy }, _environment, _patcher, _settings,
            new FakeRuntime(), _log.Add, catalog, key => key == "test_border");
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        host.OnGameLoaded();

        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(host.StateOf(broken.Uid), Is.EqualTo(IntegrationState.Incompatible));
        Assert.That(host.StateOf(healthy.Uid), Is.EqualTo(IntegrationState.Active));
        Assert.That(catalog.Evaluate(new GameItem[] { null! })[0].Single().TintRgb, Is.EqualTo(3));
        Assert.That(_log.Single(notice => notice.PluginId == broken.Uid).DecorationId, Is.EqualTo(broken.Uid + "/missing"));
    }

    [Test]
    public void DecorationCatalog_AbsenceDoesNotActivateOrLog()
    {
        _environment.Enabled.Clear();
        var catalog = new IntegrationDecorationCatalog();
        var plugin = new FakePlugin();
        var host = HostWithDecorations(catalog, plugin);

        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        host.OnGameLoaded();

        Assert.That(catalog.Evaluate(new GameItem[] { null! })[0], Is.Empty);
        Assert.That(_log, Is.Empty);
        Assert.That(plugin.Calls, Is.Empty);
        host.Dispose();
    }

    [Test]
    public void EvaluatorExceptionFaultsAndWithdrawsOnlyItsPlugin()
    {
        var catalog = new IntegrationDecorationCatalog();
        catalog.Changed += () => throw new InvalidOperationException("view callback");
        var broken = new FakePlugin
        {
            Uid = "digital_storage:broken",
            Patch = registration =>
            {
                registration.Patch(Target, postfix: Prefix);
                ((ITerminalDecorationRegistration)registration).AddSlotDecoration(
                    "marker", "broken_border", _ => throw new NotSupportedException("test details"));
            }
        };
        var healthy = new FakePlugin
        {
            Uid = "digital_storage:healthy",
            Patch = registration =>
            {
                registration.Patch(Target, prefix: Prefix);
                ((ITerminalDecorationRegistration)registration).AddSlotDecoration(
                    "marker", "healthy_border", _ => new int?[] { 0x123456 });
            }
        };
        var host = HostWithDecorations(catalog, broken, healthy);
        host.Initialize();
        host.Enable();
        host.OnModsLoaded();
        host.OnGameLoaded();

        var result = catalog.Evaluate(new GameItem[] { null! });

        Assert.That(host.StateOf(broken.PluginUid), Is.EqualTo(IntegrationState.Faulted));
        Assert.That(host.StateOf(healthy.PluginUid), Is.EqualTo(IntegrationState.Active));
        Assert.That(_patcher.Applied.Single().Prefix, Is.SameAs(Prefix));
        Assert.That(_patcher.Removed.Single().Postfix, Is.SameAs(Prefix));
        Assert.That(broken.Calls.Count(call => call == "unavailable"), Is.EqualTo(1));
        Assert.That(result[0].Select(decoration => decoration.TintRgb), Is.EqualTo(new[] { 0x123456 }));
        var fault = _log.Single(notice => notice.PluginId == broken.PluginUid && notice.State == IntegrationState.Faulted);
        Assert.That(fault.Reason, Is.EqualTo("decoration_evaluate_failed"));
        Assert.That(fault.ExceptionType, Is.EqualTo(nameof(NotSupportedException)));
        Assert.That(fault.DecorationId, Is.EqualTo(broken.Uid + "/marker"));
        host.Dispose();
    }

    [Test]
    public void Registration_RejectsInvalidIdsDuplicateIdsInvalidOverlaysAndNullCallbacks()
    {
        var registration = new IntegrationRegistration("1.0.0", new FakeRuntime(), Uid);
        Assert.Throws<ArgumentException>((Action)(() => registration.AddSlotDecoration("Bad", "digital_storage:key", _ => Array.Empty<int?>())));
        Assert.Throws<ArgumentException>((Action)(() => registration.AddSlotDecoration("valid", "digital_storage:key", _ => Array.Empty<int?>())));
        Assert.Throws<ArgumentNullException>((Action)(() => registration.AddSlotDecoration("valid", "valid_key", null!)));
        registration.AddSlotDecoration("valid", "client_demand_border", _ => Array.Empty<int?>());
        Assert.Throws<ArgumentException>((Action)(() => registration.AddSlotDecoration("valid", "client_demand_border", _ => Array.Empty<int?>())));
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
    public void UninstalledModLeftInTheEnabledList_GetsNoSettings_AndIsNeverCalled()
    {
        _environment.Installed.Clear();
        _environment.Running.Clear();
        _environment.Versions.Clear();
        var plugin = new FakePlugin();

        var host = Started(plugin);
        host.OnGameLoaded();
        host.OnSettingsApplied();
        host.Dispose();

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Absent));
        Assert.That(plugin.Calls, Is.Empty);
        Assert.That(_settings.Defined, Is.Empty, "An unsubscribed mod still in the saved mod order shows no header.");
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
        Assert.That(_settings.UiLocked[Uid], Is.False);
        Assert.That(_settings.UiLabelState[Uid], Is.Null);
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings", "register" }));
        Assert.That(_patcher.Applied.Select(p => p.Target), Is.EqualTo(new[] { Target }));
        Assert.That(_log.Select(line => line.State), Has.One.EqualTo(IntegrationState.Active));
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
        Assert.That(_log.Select(line => line.Reason), Does.Contain("enabled_unverified"));
    }

    [TestCase(true, true, null, true, true)]
    [TestCase(true, true, false, true, false)]
    [TestCase(true, false, null, false, false)]
    [TestCase(false, true, null, false, false)]
    public void UnverifiedVersion_DefaultDependsOnRunningProtocolAndRespectsSavedSwitch(
        bool running, bool protocol, bool? savedSwitch, bool expectedDefault, bool expectActive)
    {
        _environment.Versions["other_mod"] = "9.9.9";
        if (!running) _environment.Running.Clear();
        if (savedSwitch.HasValue) _settings.Values[Uid] = savedSwitch.Value;
        var probeCalls = 0;
        var plugin = new FakePlugin
        {
            DefaultProbe = () =>
            {
                probeCalls++;
                return protocol;
            }
        };

        using var host = Started(plugin);
        host.OnGameLoaded();

        Assert.That(probeCalls, Is.EqualTo(running ? 1 : 0));
        Assert.That(_settings.Defaults[Uid], Is.EqualTo(expectedDefault));
        Assert.That(_settings.Verified[Uid], Is.False);
        Assert.That(_settings.Status[Uid](), Is.EqualTo(expectedDefault ? null : IntegrationHost.StatusUnverified));
        Assert.That(host.StateOf(Uid), Is.EqualTo(expectActive ? IntegrationState.Active : IntegrationState.Disabled));
        Assert.That(plugin.Calls.Contains("available"), Is.EqualTo(expectActive));
    }

    [Test]
    public void BrewingExpansion_DefaultsOnButHonorsSavedOff()
    {
        var plugin = new BrewingExpansionPlugin();
        _environment.Enabled.Add(plugin.RequiredModId);
        _environment.Running.Add(plugin.RequiredModId);
        _environment.Installed.Add(plugin.RequiredModId);
        _environment.Versions[plugin.RequiredModId] = "3.6.8.6";
        _settings.Values[plugin.PluginUid] = false;

        using var host = Started(plugin);

        Assert.That(_settings.Defaults[plugin.PluginUid], Is.True);
        Assert.That(host.StateOf(plugin.PluginUid), Is.EqualTo(IntegrationState.Disabled));
        Assert.That(_patcher.Applied, Is.Empty);
    }

    [TestCase("3.6.8.6", null, true)]
    [TestCase("3.6.9.0", null, true)]
    [TestCase("3.6.9.0", false, false)]
    public void RunningRangeVersion_DefaultsOnAndRespectsTheSavedSwitch(string version, bool? savedSwitch, bool active)
    {
        _environment.Versions["other_mod"] = version;
        if (savedSwitch.HasValue) _settings.Values[Uid] = savedSwitch.Value;
        var plugin = new RangePlugin { DefaultProbe = () => true, Patch = r => r.Patch(Target, Prefix) };
        using var host = Started(plugin);

        Assert.That(_settings.Verified[Uid], Is.True, "The settings host is asked to use the shared switch.");
        Assert.That(_settings.Defaults[Uid], Is.True);
        Assert.That(host.StateOf(Uid), Is.EqualTo(active ? IntegrationState.Active : IntegrationState.Disabled));
        Assert.That(_settings.Status[Uid](), Is.EqualTo(active ? IntegrationHost.StatusActiveUnverified : IntegrationHost.StatusDisabled));
        Assert.That(_patcher.Applied, Has.Count.EqualTo(active ? 1 : 0));
        Assert.That(_log.Last().Verified, Is.False);
    }

    [Test]
    public void RangeVersionWithoutDefaultOptIn_StartsOffButCanBeTurnedOn()
    {
        _environment.Versions["other_mod"] = "3.6.8.6";
        using var host = Started(new RangePlugin());

        Assert.That(_settings.Defaults[Uid], Is.False);
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Disabled));

        _settings.Values[Uid] = true;
        host.OnSettingsApplied();
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Active));
    }

    [Test]
    public void RangeVersionWithoutRunningMod_StaysOffWithoutProbing()
    {
        _environment.Versions["other_mod"] = "3.6.8.6";
        _environment.Running.Clear();
        var plugin = new RangePlugin { DefaultProbe = () => throw new InvalidOperationException() };
        using var host = Started(plugin);

        Assert.That(_settings.Defaults[Uid], Is.False);
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.NotRunning));
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings" }));
    }

    [Test]
    public void DefaultOnRangeVersion_WithMissingTarget_RemainsInactive()
    {
        _environment.Versions["other_mod"] = "3.6.8.6";
        var plugin = new RangePlugin { DefaultProbe = () => true, Patch = r => r.Fail("tooltip_target_signature") };
        using var host = Started(plugin);

        Assert.That(_settings.Defaults[Uid], Is.True);
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Incompatible));
        Assert.That(_settings.Status[Uid](), Is.EqualTo(IntegrationHost.StatusIncompatible));
        Assert.That(_settings.UiLocked[Uid], Is.True);
        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(_log.Single().Reason, Is.EqualTo("tooltip_target_signature"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnsupportedRangeVersion_ReportsIncompatibleEvenWithSavedSwitch(bool on)
    {
        _environment.Versions["other_mod"] = "3.7.0";
        _settings.Values[Uid] = on;
        var plugin = new RangePlugin
        {
            DefaultProbe = () => throw new InvalidOperationException(),
            Patch = r => r.Patch(Target, Prefix)
        };
        using var host = Started(plugin);

        host.OnSettingsApplied();
        Assert.That(_settings.Defaults[Uid], Is.False);
        Assert.That(_settings.Status[Uid](), Is.EqualTo(IntegrationHost.StatusIncompatible));
        Assert.That(_settings.UiLocked[Uid], Is.True);
        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(_log.Single().Reason, Is.EqualTo("unsupported_version"));
    }

    [Test]
    public void ProtocolDefault_StillRunsRegistrationVerification()
    {
        _environment.Versions["other_mod"] = "9.9.9";
        var plugin = new FakePlugin { DefaultProbe = () => true, Patch = r => r.Verify(() => "protocol unavailable") };

        using var host = Started(plugin);
        host.OnGameLoaded();

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Incompatible));
        Assert.That(_settings.UiLocked[Uid], Is.True);
        Assert.That(_settings.UiLabelState[Uid], Is.EqualTo(IntegrationHost.StatusFailedLabel));
        Assert.That(plugin.Calls, Is.EqualTo(new[] { "settings", "register" }));
    }

    [Test]
    public void ModNotRunning_IsReportedAndNotRegistered()
    {
        _environment.Running.Clear();
        var plugin = new FakePlugin();

        var host = Started(plugin);

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.NotRunning));
        Assert.That(_settings.UiLocked[Uid], Is.True);
        Assert.That(_settings.UiLabelState[Uid], Is.EqualTo(IntegrationHost.StatusNotRunningLabel));
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

        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Faulted));
        Assert.That(_settings.Status[Uid](), Is.EqualTo(IntegrationHost.StatusFaulted));
        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(_patcher.Removed, Has.Count.EqualTo(2));
        Assert.That(_log.Last().Severity, Is.EqualTo(IntegrationSeverity.Error));
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
        Assert.That(_settings.UiLocked[Uid], Is.True);
        Assert.That(_settings.UiLabelState[Uid], Is.EqualTo(IntegrationHost.StatusFailedLabel));
        Assert.That(_patcher.Applied, Is.Empty);
        Assert.That(_log.Last().Reason, Is.EqualTo("patch_check_rejected"));
        Assert.That(_log.Last().Severity, Is.EqualTo(IntegrationSeverity.Warning));
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
        Assert.That(_settings.UiLocked["digital_storage:broken"], Is.True);
        Assert.That(_settings.UiLabelState["digital_storage:broken"], Is.EqualTo(IntegrationHost.StatusFailedLabel));
        Assert.That(host.StateOf(Uid), Is.EqualTo(IntegrationState.Active));
        Assert.That(_log.Single(line => line.PluginId == broken.Uid).ExceptionType, Is.EqualTo(nameof(InvalidOperationException)));
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
        Assert.That(_log.Count(line => line.State == IntegrationState.Active), Is.EqualTo(2));
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

        Assert.That(_log.Count(line => line.State == IntegrationState.NotRunning), Is.EqualTo(1));
    }

    [Test]
    public void Discover_CreatesMarkedPlugins_AndSkipsBrokenOnes()
    {
        var log = new List<IntegrationNotice>();

        var plugins = IntegrationHost.Discover(new[] { typeof(MarkedPlugin), typeof(UnmarkedPlugin), typeof(MarkedNotAPlugin) }, log.Add);

        Assert.That(plugins.Select(plugin => plugin.GetType()), Is.EqualTo(new[] { typeof(MarkedPlugin) }));
        Assert.That(log.Single().PluginId, Does.Contain(nameof(MarkedNotAPlugin)));
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

        internal Func<bool>? DefaultProbe { get; set; }

        internal Action? RuntimeAttach { get; set; }

        internal List<string> Calls { get; } = new();

        public override string PluginUid => Uid;

        public override string RequiredModId => "other_mod";

        public override IReadOnlyCollection<string> VerifiedVersions => new[] { "1.0.0" };

        public override string SettingSectionKey => "integration_other_section";

        public override string SettingLabelKey => "integration_other_label";

        public override string SettingTooltipKey => "integration_other_tooltip";

        public override bool CanEnableByDefault() => DefaultProbe?.Invoke() == true;

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

        public override void OnRuntimeAvailable(IDigitalStorageRuntime runtime)
        {
            Calls.Add("available");
            RuntimeAttach?.Invoke();
        }

        public override void OnRuntimeUnavailable() => Calls.Add("unavailable");
    }

    private sealed class RangePlugin : FakePlugin, IIntegrationVersionPolicy
    {
        public override IReadOnlyCollection<string> VerifiedVersions => Array.Empty<string>();

        public bool SupportsVersion(string version) => BrewingExpansionVersions.Supports(version);
    }

    private sealed class FakeEnvironment : IIntegrationEnvironment
    {
        internal HashSet<string> Enabled { get; } = new() { "other_mod" };

        internal HashSet<string> Running { get; } = new() { "other_mod" };

        internal Dictionary<string, string> Versions { get; } = new() { ["other_mod"] = "1.0.0" };

        public bool IsModEnabled(string modId) => Enabled.Contains(modId);

        public bool IsModRunning(string modId) => Running.Contains(modId);

        internal HashSet<string> Installed { get; } = new() { "other_mod" };

        public bool IsModInstalled(string modId) => Installed.Contains(modId);

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

        internal Dictionary<string, bool> Verified { get; } = new();

        internal Dictionary<string, bool> UiLocked { get; } = new();

        internal Dictionary<string, string?> UiLabelState { get; } = new();

        internal Dictionary<string, bool> Values { get; } = new();

        internal Dictionary<string, Func<string?>> Status { get; } = new();

        internal List<string> Defined { get; } = new();

        internal List<string> Options { get; } = new();

        public (Func<bool> Switch, IIntegrationConfig Config, Action<bool, string?> SetUiState) Define(
            IIntegrationPlugin plugin, string modVersion, bool verified, bool defaultEnabled, Func<string?> status)
        {
            var uid = plugin.PluginUid;
            Defined.Add(uid);
            Defaults[uid] = defaultEnabled;
            Verified[uid] = verified;
            Values.TryAdd(uid, defaultEnabled);
            Status[uid] = status;
            Action<bool, string?> setUiState = (locked, labelState) =>
            {
                UiLocked[uid] = locked;
                UiLabelState[uid] = labelState;
            };
            return (() => Values[uid], new FakeConfig(this, uid), setUiState);
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
