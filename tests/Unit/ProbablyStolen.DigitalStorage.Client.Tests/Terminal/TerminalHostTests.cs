using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class TerminalHostTests
{
    private readonly List<string> _log = new();
    private MemoryStore _store = null!;
    private RecordingHostView _view = null!;
    private TerminalHost _host = null!;
    private bool _testAvailable;

    [SetUp]
    public void Build()
    {
        _log.Clear();
        _testAvailable = true;
        _store = new MemoryStore();
        _view = new RecordingHostView();
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add);
        registry.AddTerminal(Storage());
        registry.AddTerminal(View("test", 1, _ => _testAvailable));
        registry.Freeze();
        var handler = new UniversalTerminalHandler(registry, _store, _log.Add);
        var bar = new VerticalButtonBar(registry, handler, _log.Add);
        _host = new TerminalHost(registry, handler, bar, _view, Array.Empty<string>, () => false, _log.Add, Window);
    }

    [Test]
    public void ConfiguredDefault_IsUsedForMissingAndFailedViews()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add, "partition");
        registry.AddTerminal(View("partition"));
        registry.AddTerminal(View("other"));
        registry.Freeze();
        var handler = new UniversalTerminalHandler(registry, _store, _log.Add);
        var bar = new VerticalButtonBar(registry, handler, _log.Add);
        var host = new TerminalHost(registry, handler, bar, _view, Array.Empty<string>, () => false, _log.Add, Window);

        _store.Value = "missing";
        host.Open();
        Assert.That(host.CurrentTerminal, Is.EqualTo("partition"));
        _view.FailingViews.Add("other");
        host.Switch("other");
        Assert.That(host.CurrentTerminal, Is.EqualTo("partition"));
        Assert.That(_view.Calls, Has.Member("show:partition"));
        Assert.That(_store.Written, Is.Empty);
    }

    [Test]
    public void Open_ShowsTheStoredViewWithoutWriting()
    {
        _store.Value = "test";
        _host.Open();

        Assert.That(_host.CurrentTerminal, Is.EqualTo("test"));
        Assert.That(_view.Calls, Is.EqualTo(new[] { "show:test", "refresh" }));
        Assert.That(_store.Written, Is.Empty);
    }

    [Test]
    public void Switch_RestoresEachViewsSavedState()
    {
        _host.Open();
        _view.StateByView["storage"] = "search=wood";
        _host.StoreState();
        _host.Switch("test");
        Assert.That(_view.LastRestored, Is.Null);

        _view.StateByView["test"] = "scroll=3";
        _host.StoreState();
        _host.Switch("storage");

        Assert.That(_view.LastRestored, Is.EqualTo("search=wood"));
        _host.Switch("test");
        Assert.That(_view.LastRestored, Is.EqualTo("scroll=3"));
    }

    [Test]
    public void Switch_IgnoresTheShownViewUnavailableViewsAndSubScreens()
    {
        _host.Open();
        _view.Calls.Clear();

        _host.Switch("storage");
        _testAvailable = false;
        _host.Switch("test");
        _testAvailable = true;
        _host.SwitchToScreen("modules");
        _host.Switch("test");

        Assert.That(_view.Calls, Is.EqualTo(new[] { "open:modules" }));
    }

    [Test]
    public void SubScreen_ReturnsToTheSameViewWithoutWritingTheStore()
    {
        _store.Value = "test";
        _host.Open();
        _host.Bar.Toggle(_host.Bar.TerminalSelection);
        _view.Calls.Clear();

        _host.SwitchToScreen("modules");
        Assert.That(_host.Bar.OpenSelection, Is.Null, "Entering a sub-screen closes the panel.");
        Assert.That(_host.HandleBack(), Is.True);

        Assert.That(_view.Calls, Is.EqualTo(new[] { "open:modules", "close:modules", "refresh" }));
        Assert.That(_host.CurrentTerminal, Is.EqualTo("test"));
        Assert.That(_store.Written, Is.Empty);
        Assert.That(_host.HandleBack(), Is.False, "Without a sub-screen the caller closes the window.");
    }

    [Test]
    public void SwitchToScreen_ReturnsFromAnOpenSubScreenFirst()
    {
        _host.Open();
        _view.Calls.Clear();

        _host.SwitchToScreen("a");
        _host.SwitchToScreen("b");

        Assert.That(_view.Calls, Is.EqualTo(new[] { "open:a", "close:a", "refresh", "open:b" }));
    }

    [Test]
    public void Invalidate_FallsBackToStorageWithoutWriting()
    {
        _store.Value = "test";
        _host.Open();
        _view.Calls.Clear();

        _testAvailable = false;
        _host.Invalidate();

        Assert.That(_host.CurrentTerminal, Is.EqualTo("storage"));
        Assert.That(_view.Calls, Is.EqualTo(new[] { "show:storage", "refresh" }));
        Assert.That(_store.Value, Is.EqualTo("test"));
    }

    [Test]
    public void Invalidate_UnderASubScreenWaitsForItsReturn()
    {
        _store.Value = "test";
        _host.Open();
        _host.SwitchToScreen("modules");
        _view.Calls.Clear();

        _testAvailable = false;
        _host.Invalidate();
        Assert.That(_view.Calls, Is.EqualTo(new[] { "refresh" }), "The modules page stays open.");

        _host.ReturnToParent();
        Assert.That(_view.Calls, Is.EqualTo(new[] { "refresh", "close:modules", "show:storage", "refresh" }));
        Assert.That(_host.CurrentTerminal, Is.EqualTo("storage"));
    }

    [Test]
    public void FailingView_FallsBackToStorageAndForgetsItsState()
    {
        _store.Value = "test";
        _view.FailingViews.Add("test");

        _host.Open();

        Assert.That(_host.CurrentTerminal, Is.EqualTo("storage"));
        Assert.That(_view.Calls, Is.EqualTo(new[] { "show:test", "show:storage", "refresh" }));
        Assert.That(_log, Has.Some.Contains("build=failed name=test"));
        Assert.That(_store.Written, Is.Empty);
    }

    [Test]
    public void Close_ReleasesTheSubScreenAndForgetsStates()
    {
        _host.Open();
        _view.StateByView["storage"] = "kept";
        _host.StoreState();
        _host.SwitchToScreen("modules");
        _view.Calls.Clear();

        _host.Close();
        Assert.That(_view.Calls, Is.EqualTo(new[] { "close:modules" }));
        Assert.That(_host.InSubScreen, Is.False);

        _host.Open();
        _host.Switch("test");
        _host.Switch("storage");
        Assert.That(_view.LastRestored, Is.Null, "States last only while the window is open.");
    }

    [Test]
    public void SettingChanges_ReachTheViewAndTheRail()
    {
        _host.Open();
        _view.Calls.Clear();

        new SettingToggleButton<Shade>(ShadeSetting(), new MemoryConfig()).OnPress(_host.Context, false);

        Assert.That(_view.Calls, Is.EqualTo(new[] { "setting:shade", "refresh" }));
    }

    [Test]
    public void SubScreenThatFailsToOpen_ReturnsToTheViewFromInsideOpen()
    {
        // TerminalWindow.OpenSubScreen calls ReturnToParent itself when the page cannot be shown.
        _host.Open();
        _view.Calls.Clear();
        _view.OnOpen = _ => _host.ReturnToParent();

        _host.SwitchToScreen("page");

        Assert.That(_host.InSubScreen, Is.False);
        Assert.That(_host.SubScreen, Is.Null);
        Assert.That(_host.CurrentTerminal, Is.EqualTo("storage"));
        Assert.That(_view.Calls, Is.EqualTo(new[] { "open:page", "close:page", "refresh" }));
        Assert.That(_host.HandleBack(), Is.False, "nothing is left open for the back key");
    }

    [Test]
    public void SubScreenThatFailsToOpen_StillAppliesAPendingFallback()
    {
        _store.Value = "test";
        _host.Open();
        _view.OnOpen = _ =>
        {
            // The view's module went away before the page gave up.
            _testAvailable = false;
            _host.Invalidate();
            _host.ReturnToParent();
        };

        _host.SwitchToScreen("page");

        Assert.That(_host.InSubScreen, Is.False);
        Assert.That(_host.CurrentTerminal, Is.EqualTo("storage"));
        Assert.That(_view.Shown, Is.EqualTo("storage"));
        Assert.That(_store.Value, Is.EqualTo("test"), "The fallback does not rewrite the stored view.");
    }
}
