using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
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
        var registry = Registry(log: _log.Add);
        registry.AddTerminal(Storage());
        registry.AddTerminal(View("test", 1, _ => _testAvailable));
        registry.Freeze();
        var handler = new UniversalTerminalHandler(registry, _store, _log.Add);
        var docks = new TerminalDocks(registry, handler, _log.Add);
        _host = new TerminalHost(registry, handler, docks, _view, Array.Empty<string>, () => false, _log.Add, Window);
    }

    [Test]
    public void ConfiguredDefault_IsUsedForMissingAndFailedViews()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add, "partition");
        registry.AddDock(DockSide.Left, "left_title");
        registry.AddTerminal(View("partition"));
        registry.AddTerminal(View("other"));
        registry.Freeze();
        var handler = new UniversalTerminalHandler(registry, _store, _log.Add);
        var docks = new TerminalDocks(registry, handler, _log.Add);
        var host = new TerminalHost(registry, handler, docks, _view, Array.Empty<string>, () => false, _log.Add, Window);

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
        _host.PushScreen(new TestScreen("modules"));
        _host.Switch("test");

        Assert.That(_view.Calls, Is.EqualTo(new[] { "open:modules", "refresh" }));
    }

    [Test]
    public void SubScreen_ReturnsToTheSameViewWithoutWritingTheStore()
    {
        _store.Value = "test";
        _host.Open();
        var left = _host.Docks.Find(DockSide.Left)!;
        left.Toggle(left.TerminalSelection!);
        _view.Calls.Clear();

        _host.PushScreen(new TestScreen("modules"));
        Assert.That(_host.Docks.OpenSelection, Is.Null, "Entering a sub-screen closes the panel.");
        Assert.That(_host.HandleBack(), Is.True);

        Assert.That(_view.Calls, Is.EqualTo(new[] { "open:modules", "refresh", "close:modules", "refresh" }));
        Assert.That(_host.CurrentTerminal, Is.EqualTo("test"));
        Assert.That(_store.Written, Is.Empty);
        Assert.That(_host.HandleBack(), Is.False, "Without a sub-screen the caller closes the window.");
    }

    [Test]
    public void PushScreen_StacksPagesAndBackReturnsOneAtATime()
    {
        _host.Open();
        _view.Calls.Clear();
        var list = new TestScreen("list");
        var editor = new TestScreen("editor");

        _host.PushScreen(list);
        _host.PushScreen(editor);
        Assert.That(_host.TopScreen, Is.SameAs(editor));
        Assert.That(_host.Context.ScreenId, Is.EqualTo("editor"));
        Assert.That(_host.Context.ScreenDepth, Is.EqualTo(2));

        Assert.That(_host.HandleBack(), Is.True);
        Assert.That(_host.TopScreen, Is.SameAs(list));
        Assert.That(_host.HandleBack(), Is.True);
        Assert.That(_host.InSubScreen, Is.False);
        Assert.That(_host.Context.ScreenId, Is.Null);

        Assert.That(_view.Calls, Is.EqualTo(new[]
        {
            "open:list", "refresh", "open:editor/over:list", "refresh",
            "close:editor/reveal:list", "refresh", "close:list", "refresh"
        }));
    }

    [Test]
    public void PushScreen_RefusesTheSamePageTwice()
    {
        _host.Open();
        var page = new TestScreen("page");
        _host.PushScreen(page);

        Assert.Throws<InvalidOperationException>(new Action(() => _host.PushScreen(page)));
        Assert.That(_host.Screens, Has.Count.EqualTo(1));
    }

    [Test]
    public void PopToView_ClosesEveryPageFromTheTop()
    {
        _host.Open();
        _host.PushScreen(new TestScreen("a"));
        _host.PushScreen(new TestScreen("b"));
        _view.Calls.Clear();

        _host.PopToView();

        Assert.That(_host.InSubScreen, Is.False);
        Assert.That(_view.Calls, Is.EqualTo(new[] { "close:b/reveal:a", "refresh", "close:a", "refresh" }));
    }

    [Test]
    public void Selection_ComesFromTheViewOnlyOverTheView()
    {
        _host.Open();
        var entry = new DisplayEntry("g1", new[] { new TerminalEntry(new ItemKey("apple"), 1, "Apple", "a") });
        _view.Selection = new[] { entry };

        Assert.That(_host.Context.Selection, Is.EqualTo(new[] { entry }));
        _host.PushScreen(new TestScreen("page"));
        Assert.That(_host.Context.Selection, Is.Empty, "A sub-screen hides the view's selection.");
    }

    [Test]
    public void ClearSelection_ClearsTheViewAndRefreshesTheDocks()
    {
        _host.Open();
        _view.Calls.Clear();

        _host.ClearSelection();

        Assert.That(_view.Calls, Is.EqualTo(new[] { "clear-selection", "refresh" }));
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
    public void Invalidate_UnderSubScreensWaitsForTheLastReturn()
    {
        _store.Value = "test";
        _host.Open();
        _host.PushScreen(new TestScreen("modules"));
        _host.PushScreen(new TestScreen("detail"));
        _view.Calls.Clear();

        _testAvailable = false;
        _host.Invalidate();
        Assert.That(_view.Calls, Is.EqualTo(new[] { "refresh" }), "The pages stay open.");

        _host.PopScreen();
        Assert.That(_view.Calls, Does.Not.Contain("show:storage"));
        _host.PopScreen();
        Assert.That(_view.Calls, Does.Contain("show:storage"));
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
    public void Close_ReleasesThePagesFromTheTopAndForgetsStates()
    {
        _host.Open();
        _view.StateByView["storage"] = "kept";
        _host.StoreState();
        _host.PushScreen(new TestScreen("a"));
        _host.PushScreen(new TestScreen("b"));
        _view.Calls.Clear();

        _host.Close();
        Assert.That(_view.Calls, Is.EqualTo(new[] { "close:b/reveal:a", "close:a" }));
        Assert.That(_host.InSubScreen, Is.False);

        _host.Open();
        _host.Switch("test");
        _host.Switch("storage");
        Assert.That(_view.LastRestored, Is.Null, "States last only while the window is open.");
    }

    [Test]
    public void SettingChanges_ReachTheViewAndTheDocks()
    {
        _host.Open();
        _view.Calls.Clear();

        new SettingToggleButton<Shade>(ShadeSetting(), new MemoryConfig()).OnPress(_host.Context, false);

        Assert.That(_view.Calls, Is.EqualTo(new[] { "setting:shade", "refresh" }));
    }

    [Test]
    public void SubScreenThatFailsToOpen_IsNotPushed()
    {
        _host.Open();
        _host.PushScreen(new TestScreen("list"));
        _view.Calls.Clear();
        _view.FailingScreens.Add("page");

        _host.PushScreen(new TestScreen("page"));

        Assert.That(_host.TopScreen?.ScreenId, Is.EqualTo("list"));
        Assert.That(_view.Calls, Is.EqualTo(new[] { "open:page/over:list", "refresh" }));
        Assert.That(_log, Has.Some.Contains("open=failed id=page"));
    }

    [Test]
    public void SubScreenThatFailsToOpen_StillAppliesAFallbackRaisedMeanwhile()
    {
        _store.Value = "test";
        _host.Open();
        _view.FailingScreens.Add("page");
        _view.OnOpen = _ =>
        {
            // The view's module went away before the page gave up.
            _testAvailable = false;
            _host.Invalidate();
        };

        _host.PushScreen(new TestScreen("page"));

        Assert.That(_host.InSubScreen, Is.False);
        Assert.That(_host.CurrentTerminal, Is.EqualTo("storage"));
        Assert.That(_view.Shown, Is.EqualTo("storage"));
        Assert.That(_store.Value, Is.EqualTo("test"), "The fallback does not rewrite the stored view.");
    }
}
