using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

internal enum Shade
{
    Light,
    Medium,
    Dark
}

internal sealed class MemoryConfig : IConfigManager
{
    private readonly Dictionary<string, object> _values = new();

    public int Writes { get; private set; }

    public T GetSetting<T>(Setting<T> setting) where T : struct, Enum =>
        _values.TryGetValue(setting.Name, out var value) ? (T)value : setting.Values[0];

    public void PutSetting<T>(Setting<T> setting, T value) where T : struct, Enum
    {
        _values[setting.Name] = value;
        Writes++;
    }

    public void Force<T>(string name, T value) where T : struct, Enum => _values[name] = value;
}

internal sealed class MemoryStore : ICurrentTerminalStore
{
    public string? Value { get; set; }

    public bool FailRead { get; set; }

    public bool FailWrite { get; set; }

    public List<string> Written { get; } = new();

    public string? Read() => FailRead ? throw new InvalidOperationException("read") : Value;

    public void Write(string terminalName)
    {
        if (FailWrite) throw new InvalidOperationException("write");
        Written.Add(terminalName);
        Value = terminalName;
    }
}

internal sealed class RecordingCommands : ITerminalCommands
{
    public List<string> Calls { get; } = new();

    public void PushScreen(ITerminalScreen screen) => Calls.Add("screen:" + screen.ScreenId);

    public void PopScreen() => Calls.Add("pop");

    public void PopToView() => Calls.Add("pop-all");

    public void ClearSelection() => Calls.Add("clear-selection");

    public void ShowMessage(string textKey) => Calls.Add("message:" + textKey);

    public void StoreState() => Calls.Add("store");

    public void Switch(string terminalName) => Calls.Add("switch:" + terminalName);

    public void NotifySettingChanged(string settingName) => Calls.Add("setting:" + settingName);
}

internal sealed class RecordingHostView : ITerminalHostView
{
    public List<string> Calls { get; } = new();

    public HashSet<string> FailingViews { get; } = new();

    public Dictionary<string, object?> StateByView { get; } = new();

    public string? Shown { get; private set; }

    public object? LastRestored { get; private set; }

    public bool ShowTerminal(TerminalDefinition terminal, object? state)
    {
        Calls.Add("show:" + terminal.TerminalName);
        if (FailingViews.Contains(terminal.TerminalName)) return false;
        Shown = terminal.TerminalName;
        LastRestored = state;
        return true;
    }

    public object? StoreState()
    {
        Calls.Add("store:" + Shown);
        return Shown != null && StateByView.TryGetValue(Shown, out var state) ? state : null;
    }

    /// <summary>Screens the platform cannot show; opening one reports false.</summary>
    public HashSet<string> FailingScreens { get; } = new();

    public IReadOnlyList<DisplayEntry> Selection { get; set; } = Array.Empty<DisplayEntry>();

    public void ShowMessage(string textKey) => Calls.Add("message:" + textKey);

    public void ClearSelection()
    {
        Calls.Add("clear-selection");
        Selection = Array.Empty<DisplayEntry>();
    }

    /// <summary>Runs inside OpenScreen, before it reports.</summary>
    public Action<ITerminalScreen>? OnOpen { get; set; }

    public bool OpenScreen(ITerminalScreen screen, ITerminalScreen? covered)
    {
        Calls.Add("open:" + screen.ScreenId + (covered == null ? string.Empty : "/over:" + covered.ScreenId));
        OnOpen?.Invoke(screen);
        return !FailingScreens.Contains(screen.ScreenId);
    }

    public void CloseScreen(ITerminalScreen screen, ITerminalScreen? revealed) =>
        Calls.Add("close:" + screen.ScreenId + (revealed == null ? string.Empty : "/reveal:" + revealed.ScreenId));

    public void RefreshButtons() => Calls.Add("refresh");

    public void SettingChanged(string settingName) => Calls.Add("setting:" + settingName);
}

internal static class TerminalFixtures
{
    public static readonly ButtonAppearance Appearance = new("label", "tooltip");

    public static TerminalDefinition View(string name, int order = 0, Func<TerminalContext, bool>? available = null) =>
        new(name, order, name + "_name", available ?? (_ => true), _ => new object());

    public static TerminalDefinition Storage() => View(TerminalRegistry.StorageTerminalName, int.MinValue);

    public static TerminalAction Action(string id, int order = 0, ActionPlacement placement = ActionPlacement.Rail,
        ActionScope? scope = null, Func<TerminalContext, bool>? visible = null, Action<TerminalContext>? press = null,
        DockSide side = DockSide.Left) =>
        new(id, order, side, placement, scope ?? ActionScope.AllViews, visible ?? (_ => true), _ => Appearance,
            press ?? (_ => { }));

    /// <summary>Creates a registry with both docks, as the storage terminal registers them.</summary>
    public static TerminalRegistry Registry(IConfigManager? config = null, Action<string>? log = null)
    {
        var registry = new TerminalRegistry(config ?? new MemoryConfig(), log ?? (_ => { }));
        registry.AddDock(DockSide.Left, "left_title");
        registry.AddDock(DockSide.Right, "right_title");
        return registry;
    }

    public static Setting<Shade> ShadeSetting(string name = "shade") =>
        new(name, new[] { Shade.Light, Shade.Medium, Shade.Dark },
            value => new ButtonAppearance("shade_" + value.ToString().ToLowerInvariant(), "shade_tip"));

    public static TerminalContext Context(string current = TerminalRegistry.StorageTerminalName,
        bool testView = false, ITerminalCommands? commands = null, params string[] modules) =>
        new(modules, testView, current, commands ?? new RecordingCommands(), Window);

    public static TerminalContext ScreenContext(string screenId, int depth = 1,
        string current = TerminalRegistry.StorageTerminalName) =>
        new(Array.Empty<string>(), false, current, new RecordingCommands(), Window, screenId: screenId, screenDepth: depth);

    public static readonly object Window = new();
}

internal sealed class TestScreen : ITerminalScreen
{
    public TestScreen(string screenId) => ScreenId = screenId;

    public string ScreenId { get; }

    public override string ToString() => ScreenId;
}
