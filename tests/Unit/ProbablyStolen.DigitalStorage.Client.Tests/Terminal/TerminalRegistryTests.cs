using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class TerminalRegistryTests
{
    private readonly List<string> _log = new();

    [SetUp]
    public void Clear() => _log.Clear();

    [Test]
    public void Terminals_SortByOrderThenName()
    {
        var registry = New();
        registry.AddTerminal(View("zeta", 5));
        registry.AddTerminal(View("alpha", 5));
        registry.AddTerminal(Storage());

        Assert.That(registry.Terminals.Select(terminal => terminal.TerminalName),
            Is.EqualTo(new[] { "storage", "alpha", "zeta" }));
    }

    [Test]
    public void DuplicateNamesAndIds_AreRefusedAndLogged()
    {
        var registry = New();

        Assert.That(registry.AddTerminal(View("a")), Is.True);
        Assert.That(registry.AddTerminal(View("a")), Is.False);
        Assert.That(registry.AddAction(Action("x")), Is.True);
        Assert.That(registry.AddAction(Action("x")), Is.False);
        Assert.That(registry.AddSetting(ShadeSetting("x"), 0, DockSide.Left, ActionPlacement.Rail, ActionScope.AllViews), Is.False);

        Assert.That(registry.Terminals, Has.Count.EqualTo(1));
        Assert.That(registry.Buttons, Has.Count.EqualTo(1));
        Assert.That(_log, Has.Count.EqualTo(3));
        Assert.That(_log, Has.All.Contains("duplicate"));
    }

    [Test]
    public void FinalBackGuards_DefaultAllowRejectDuplicatesAndApplyDenials()
    {
        var registry = New();
        registry.AddTerminal(Storage());
        Assert.That(registry.AddFinalBackGuard("keep", () => false), Is.True);
        Assert.That(registry.AddFinalBackGuard("keep", () => true), Is.False);
        registry.Freeze();

        Assert.That(registry.AllowsFinalBackClose(), Is.False);
        Assert.That(_log, Has.Some.Contains("duplicate"));
    }

    [Test]
    public void FinalBackGuardFailureLogsAndAllowsOtherGuardsToRun()
    {
        var registry = New();
        registry.AddTerminal(Storage());
        registry.AddFinalBackGuard("broken", () => throw new InvalidOperationException());
        registry.AddFinalBackGuard("deny", () => false);
        registry.Freeze();

        Assert.That(registry.AllowsFinalBackClose(), Is.False);
        Assert.That(_log, Has.Some.Contains("back_guard_failed"));
    }

    [Test]
    public void Freeze_RequiresTheStorageTerminalAndClosesRegistration()
    {
        var registry = New();
        Assert.Throws<InvalidOperationException>(new Action(registry.Freeze));

        registry.AddTerminal(Storage());
        registry.Freeze();

        Assert.Throws<InvalidOperationException>(new Action(() => { _ = registry.AddTerminal(View("late")); }));
        Assert.Throws<InvalidOperationException>(new Action(() => { _ = registry.AddAction(Action("late")); }));
        Assert.That(registry.StorageTerminal.TerminalName, Is.EqualTo(TerminalRegistry.StorageTerminalName));
    }

    [Test]
    public void Freeze_RequiresConfiguredDefaultAndClosesRegistration()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add, "partition");
        registry.AddDock(DockSide.Left, "left_title");
        registry.AddTerminal(Storage());
        Assert.Throws<InvalidOperationException>(new Action(registry.Freeze));
        registry.AddTerminal(View("partition"));
        registry.Freeze();

        Assert.That(registry.DefaultTerminal.TerminalName, Is.EqualTo("partition"));
        Assert.Throws<InvalidOperationException>(new Action(() => { _ = registry.AddTerminal(View("late")); }));
    }

    [Test]
    public void AddSetting_WrapsTheSettingInAToggleWithItsNameAsId()
    {
        var registry = New();
        registry.AddSetting(ShadeSetting(), 3, DockSide.Right, ActionPlacement.Picker, ActionScope.View("storage"));
        var entry = registry.Buttons.Single();

        Assert.That(entry.Button, Is.InstanceOf<SettingToggleButton<Shade>>());
        Assert.That(entry.Button.Id, Is.EqualTo("shade"));
        Assert.That(entry.Order, Is.EqualTo(3));
        Assert.That(entry.Placement, Is.EqualTo(ActionPlacement.Picker));
        Assert.That(entry.Side, Is.EqualTo(DockSide.Right));
        Assert.That(entry.Scope, Is.EqualTo(ActionScope.View("storage")));
    }

    [Test]
    public void ArbitraryNamesRemainDistinctAndButtonsKeepRegistrationOrder()
    {
        var registry = New();
        registry.AddTerminal(Storage());
        Assert.That(registry.AddTerminal(View("a.b")), Is.True);
        Assert.That(registry.AddTerminal(View("a_b")), Is.True);
        Assert.That(registry.AddTerminal(View("A:b")), Is.True);
        registry.AddAction(Action("a.b"));
        registry.AddAction(Action("a_b"));
        registry.Freeze();
        Assert.That(registry.Find("a.b")!.TerminalName, Is.EqualTo("a.b"));
        Assert.That(registry.Find("a_b")!.TerminalName, Is.EqualTo("a_b"));
        Assert.That(registry.Find("A:b")!.TerminalName, Is.EqualTo("A:b"));
        Assert.That(registry.Buttons.Select(entry => entry.Button.Id), Is.EqualTo(new[] { "a.b", "a_b" }));
    }

    [Test]
    public void Docks_OnePerSideWithTitles()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add);

        Assert.That(registry.AddDock(DockSide.Right, "actions"), Is.True);
        Assert.That(registry.AddDock(DockSide.Left, "functions"), Is.True);
        Assert.That(registry.AddDock(DockSide.Right, "again"), Is.False);

        Assert.That(registry.Docks.Select(dock => (dock.Side, dock.TitleKey)),
            Is.EqualTo(new[] { (DockSide.Left, "functions"), (DockSide.Right, "actions") }));
        Assert.That(_log.Single(), Does.Contain("refused=dock"));
    }

    [Test]
    public void DockTitles_OverrideTheDockTitleWhileTheirScopeApplies()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add);
        registry.AddDock(DockSide.Left, "functions");
        Assert.That(registry.AddDockTitle(DockSide.Left, ActionScope.Screen("editor"), "actions"), Is.True);
        Assert.That(registry.AddDockTitle(DockSide.Left, ActionScope.Screen("editor"), "again"), Is.False);
        var left = registry.FindDock(DockSide.Left)!;

        Assert.That(registry.DockTitleKey(left, Context()), Is.EqualTo("functions"));
        Assert.That(registry.DockTitleKey(left, ScreenContext("editor")), Is.EqualTo("actions"));
        Assert.That(registry.DockTitleKey(left, ScreenContext("other")), Is.EqualTo("functions"));
        Assert.That(_log.Single(), Does.Contain("refused=dock_title"));
    }

    [Test]
    public void Freeze_RequiresADockForEveryTitleSide()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add);
        registry.AddDock(DockSide.Left, "functions");
        registry.AddTerminal(Storage());
        registry.AddDockTitle(DockSide.Right, ActionScope.AllViews, "actions");

        Assert.Throws<InvalidOperationException>(new Action(registry.Freeze));
    }

    [Test]
    public void Freeze_RequiresADockForEveryButtonSide()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add);
        registry.AddDock(DockSide.Left, "functions");
        registry.AddTerminal(Storage());
        registry.AddAction(Action("orphan", side: DockSide.Right));

        Assert.Throws<InvalidOperationException>(new Action(registry.Freeze));
    }

    [Test]
    public void ActionScope_MatchesViewsAndScreens()
    {
        var overStorage = Context();
        var overTest = Context("test");
        var onEditor = ScreenContext("editor");

        Assert.That(ActionScope.AllViews.Includes(overStorage), Is.True);
        Assert.That(ActionScope.AllViews.Includes(onEditor), Is.False);
        Assert.That(ActionScope.View("storage").Includes(overStorage), Is.True);
        Assert.That(ActionScope.View("storage").Includes(overTest), Is.False);
        Assert.That(ActionScope.View("storage").Includes(onEditor), Is.False);
        Assert.That(ActionScope.Screen("editor").Includes(onEditor), Is.True);
        Assert.That(ActionScope.Screen("editor").Includes(overStorage), Is.False);
        Assert.That(ActionScope.Screen("list").Includes(onEditor), Is.False);
    }

    private TerminalRegistry New() => Registry(log: _log.Add);
}
