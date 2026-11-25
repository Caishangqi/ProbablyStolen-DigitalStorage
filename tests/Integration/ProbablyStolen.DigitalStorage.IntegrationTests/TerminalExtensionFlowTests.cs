using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Diagnostics;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.TerminalStyle;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

/// <summary>
/// Real registry, features, handler, bar and host across window lifetimes; only the item tag, the global config and the
/// Unity view are replaced by memory doubles. Proves client collaboration only, not native UI or save behaviour.
/// </summary>
[TestFixture]
public sealed class TerminalExtensionFlowTests
{
    private readonly List<string> _log = new();
    private readonly MemoryConfig _config = new();
    private TerminalRegistry _registry = null!;
    private bool _testView;

    [SetUp]
    public void Compose()
    {
        _log.Clear();
        _testView = true;
        _registry = new TerminalRegistry(_config, _log.Add);
        StorageTerminalFeature.Register(_registry, _ => "storage-view");
        ModulesFeature.Register(_registry, _ => new NamedScreen("modules-screen"));
        TerminalStyleFeature.Register(_registry);
        DiagnosticsFeature.Register(_registry, _ => "diagnostics-view", _ => new NamedScreen("diagnostics-screen"), _log.Add);
        _registry.Freeze();
    }

    [Test]
    public void ChosenView_IsShownAgainAfterTheWindowReopens()
    {
        var item = new MemoryStore();
        var first = Open(item, out var view);
        Choose(first, DiagnosticsFeature.TerminalName);
        Assert.That(view.Shown, Is.EqualTo(DiagnosticsFeature.TerminalName));
        first.Close();

        var second = Open(item, out var reopened);

        Assert.That(second.CurrentTerminal, Is.EqualTo(DiagnosticsFeature.TerminalName));
        Assert.That(reopened.Shown, Is.EqualTo(DiagnosticsFeature.TerminalName));
        Assert.That(item.Value, Is.EqualTo(DiagnosticsFeature.TerminalName));
    }

    [Test]
    public void LostView_FallsBackAndReturnsOnceAvailableAgain()
    {
        var item = new MemoryStore();
        var host = Open(item, out _);
        Choose(host, DiagnosticsFeature.TerminalName);

        _testView = false;
        host.Invalidate();
        Assert.That(host.CurrentTerminal, Is.EqualTo(TerminalRegistry.StorageTerminalName));
        host.Close();
        Assert.That(Open(item, out _).CurrentTerminal, Is.EqualTo(TerminalRegistry.StorageTerminalName));
        Assert.That(item.Value, Is.EqualTo(DiagnosticsFeature.TerminalName), "The stored view is kept for when it is available again.");

        _testView = true;
        Assert.That(Open(item, out _).CurrentTerminal, Is.EqualTo(DiagnosticsFeature.TerminalName));
    }

    [Test]
    public void Rail_FollowsTheShownView()
    {
        var host = Open(new MemoryStore(), out _);
        Assert.That(Ids(host), Is.EqualTo(new[]
        {
            ModulesFeature.ActionId, TerminalStyleFeature.SettingName, StorageTerminalFeature.SortByName, StorageTerminalFeature.SortDirectionName,
            TerminalSelectionButton.ButtonId
        }));

        Choose(host, DiagnosticsFeature.TerminalName);
        Assert.That(Ids(host), Is.EqualTo(new[]
        {
            ModulesFeature.ActionId, TerminalStyleFeature.SettingName, DiagnosticsFeature.RailActionId,
            ActionSelectionButton.ButtonId, TerminalSelectionButton.ButtonId
        }));
    }

    [Test]
    public void ModulesPage_ReturnsToTheViewAndSettingsReachTheView()
    {
        var item = new MemoryStore();
        var host = Open(item, out var view);

        Left(host).Press(host.Context, ModulesFeature.ActionId, false);
        Assert.That(host.TopScreen?.ScreenId, Is.EqualTo("modules-screen"));
        Assert.That(host.HandleBack(), Is.True);
        Assert.That(view.Calls, Does.Contain("close:modules-screen"));
        Assert.That(item.Written, Is.Empty, "Sub-screens do not write the stored view.");

        Left(host).Press(host.Context, StorageTerminalFeature.SortByName, false);
        Assert.That(_config.GetSetting(StorageTerminalFeature.SortBy), Is.EqualTo(TerminalSortOrder.Amount));
        Assert.That(view.Calls, Does.Contain("setting:" + StorageTerminalFeature.SortByName));
    }

    private TerminalHost Open(MemoryStore item, out MemoryHostView view)
    {
        view = new MemoryHostView();
        var handler = new UniversalTerminalHandler(_registry, item, _log.Add);
        var docks = new TerminalDocks(_registry, handler, _log.Add);
        var host = new TerminalHost(_registry, handler, docks, view, Array.Empty<string>, () => _testView, _log.Add,
            new object());
        host.Open();
        return host;
    }

    private static void Choose(TerminalHost host, string terminalName)
    {
        Left(host).Press(host.Context, TerminalSelectionButton.ButtonId, false);
        Left(host).Select(host.Context, terminalName);
    }

    private static VerticalButtonBar Left(TerminalHost host) => host.Docks.Find(DockSide.Left)!;

    private static string[] Ids(TerminalHost host) => Left(host).GetButtons(host.Context).Select(button => button.Id).ToArray();

    private sealed class NamedScreen : ITerminalScreen
    {
        public NamedScreen(string screenId) => ScreenId = screenId;

        public string ScreenId { get; }
    }

    private sealed class MemoryConfig : IConfigManager
    {
        private readonly Dictionary<string, object> _values = new();

        public T GetSetting<T>(Setting<T> setting) where T : struct, Enum =>
            _values.TryGetValue(setting.Name, out var value) ? (T)value : setting.Values[0];

        public void PutSetting<T>(Setting<T> setting, T value) where T : struct, Enum => _values[setting.Name] = value;
    }

    private sealed class MemoryStore : ICurrentTerminalStore
    {
        public string? Value { get; private set; }

        public List<string> Written { get; } = new();

        public string? Read() => Value;

        public void Write(string terminalName)
        {
            Written.Add(terminalName);
            Value = terminalName;
        }
    }

    private sealed class MemoryHostView : ITerminalHostView
    {
        public List<string> Calls { get; } = new();

        public string? Shown { get; private set; }

        public bool ShowTerminal(TerminalDefinition terminal, object? state)
        {
            Shown = terminal.TerminalName;
            Calls.Add("show:" + terminal.CreateView(new object()));
            return true;
        }

        public object? StoreState() => null;

        public IReadOnlyList<DisplayEntry> Selection => Array.Empty<DisplayEntry>();

        public void ClearSelection() => Calls.Add("clear-selection");

        public void ShowMessage(string textKey) => Calls.Add("message:" + textKey);

        public bool OpenScreen(ITerminalScreen screen, ITerminalScreen? covered)
        {
            Calls.Add("open:" + screen.ScreenId);
            return true;
        }

        public void CloseScreen(ITerminalScreen screen, ITerminalScreen? revealed) => Calls.Add("close:" + screen.ScreenId);

        public void RefreshButtons() => Calls.Add("refresh");

        public void SettingChanged(string settingName) => Calls.Add("setting:" + settingName);
    }
}
