using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class VerticalButtonBarTests
{
    private readonly List<string> _log = new();
    private TerminalRegistry _registry = null!;
    private MemoryStore _store = null!;
    private UniversalTerminalHandler _handler = null!;
    private TerminalDocks _docks = null!;
    private VerticalButtonBar _bar = null!;
    private VerticalButtonBar _right = null!;
    private bool _testAvailable;

    [SetUp]
    public void Build()
    {
        _log.Clear();
        _testAvailable = false;
        _store = new MemoryStore();
        _registry = Registry(log: _log.Add);
        _registry.AddTerminal(Storage());
        _registry.AddTerminal(View("test", 1, _ => _testAvailable));
    }

    [Test]
    public void StorageView_ShowsSharedButtonsThenItsOwnByOrderThenId()
    {
        _registry.AddAction(Action("modules", 0));
        _registry.AddSetting(ShadeSetting("size"), 10, DockSide.Left, ActionPlacement.Rail, ActionScope.AllViews);
        _registry.AddSetting(ShadeSetting("sort"), 0, DockSide.Left, ActionPlacement.Rail, ActionScope.View("storage"));
        _registry.AddSetting(ShadeSetting("direction"), 10, DockSide.Left, ActionPlacement.Rail, ActionScope.View("storage"));
        _registry.AddAction(Action("test.rail", 0, scope: ActionScope.View("test")));
        Freeze();

        Assert.That(Ids(Context()), Is.EqualTo(new[] { "modules", "size", "sort", "direction" }));
        Assert.That(Ids(Context("test")), Is.EqualTo(new[] { "modules", "size", "test.rail" }));
    }

    [Test]
    public void SharedRailActions_SitBetweenFixedAndViewButtons()
    {
        _registry.AddAction(Action("b-shared", 5));
        _registry.AddAction(Action("a-shared", 5));
        _registry.AddAction(Action("own", -100, scope: ActionScope.View("storage")));
        Freeze();

        Assert.That(Ids(Context()), Is.EqualTo(new[] { "a-shared", "b-shared", "own" }));
    }

    [Test]
    public void ActionSelection_AppearsOnlyWithAPickerButtonForTheShownView()
    {
        _registry.AddAction(Action("other.picker", placement: ActionPlacement.Picker, scope: ActionScope.View("test")));
        Freeze();
        Assert.That(Ids(Context()), Does.Not.Contain(ActionSelectionButton.ButtonId),
            "A picker button of another view does not count.");
        Assert.That(Ids(Context("test")), Does.Contain(ActionSelectionButton.ButtonId));
        Assert.That(_bar.GetPickerButtons(Context("test")).Single().Id, Is.EqualTo("other.picker"));
    }

    [Test]
    public void TerminalSelection_AppearsWithTwoAvailableViewsAndComesLast()
    {
        _registry.AddAction(Action("p", placement: ActionPlacement.Picker));
        Freeze();
        Assert.That(Ids(Context()), Does.Not.Contain(TerminalSelectionButton.ButtonId));

        _testAvailable = true;
        Assert.That(Ids(Context()).TakeLast(2),
            Is.EqualTo(new[] { ActionSelectionButton.ButtonId, TerminalSelectionButton.ButtonId }));
    }

    [Test]
    public void FailingButtons_AreSkippedAndLoggedOnce()
    {
        _registry.AddAction(Action("bad", visible: _ => throw new InvalidOperationException()));
        _registry.AddAction(new TerminalAction("ugly", 0, DockSide.Left, ActionPlacement.Rail, ActionScope.AllViews, _ => true,
            _ => throw new InvalidOperationException(), _ => { }));
        _registry.AddAction(Action("good"));
        Freeze();

        Assert.That(Ids(Context()), Is.EqualTo(new[] { "good" }));
        Assert.That(Ids(Context()), Is.EqualTo(new[] { "good" }));
        Assert.That(_log, Has.Count.EqualTo(2));
    }

    [Test]
    public void Press_DropsAFailingClickAndKeepsWorking()
    {
        var pressed = 0;
        _registry.AddAction(Action("bad", press: _ => throw new InvalidOperationException()));
        _registry.AddAction(Action("good", press: _ => pressed++));
        Freeze();

        Assert.DoesNotThrow(new Action(() => _bar.Press(Context(), "bad", false)));
        _bar.Press(Context(), "good", false);

        Assert.That(pressed, Is.EqualTo(1));
        Assert.That(_log.Single(), Does.Contain("id=bad"));
    }

    [Test]
    public void SelectionPanels_ToggleAndExcludeEachOther()
    {
        _registry.AddAction(Action("p", placement: ActionPlacement.Picker));
        _testAvailable = true;
        Freeze();

        _bar.Press(Context(), ActionSelectionButton.ButtonId, false);
        Assert.That(_bar.ActionSelection.MenuOpen, Is.True);
        Assert.That(_bar.GetButtons(Context()).Single(b => b.Id == ActionSelectionButton.ButtonId).Selected, Is.True);

        _bar.Press(Context(), TerminalSelectionButton.ButtonId, false);
        Assert.That(_bar.ActionSelection.MenuOpen, Is.False);
        Assert.That(_bar.TerminalSelection!.MenuOpen, Is.True);

        _bar.Press(Context(), TerminalSelectionButton.ButtonId, false);
        Assert.That(_bar.OpenSelection, Is.Null);
    }

    [Test]
    public void ActionSelection_ClosesBeforeRunningTheChosenButton()
    {
        bool? openWhilePressed = null;
        _registry.AddAction(Action("p", placement: ActionPlacement.Picker,
            press: _ => openWhilePressed = _bar.OpenSelection != null));
        Freeze();

        _bar.Press(Context(), ActionSelectionButton.ButtonId, false);
        _bar.Select(Context(), "p");

        Assert.That(openWhilePressed, Is.False);
    }

    [Test]
    public void TerminalSelection_StoresClosesWritesAndSwitches()
    {
        _testAvailable = true;
        Freeze();
        var commands = new RecordingCommands();
        var context = Context(commands: commands);
        _bar.Press(context, TerminalSelectionButton.ButtonId, false);

        Assert.That(_bar.GetOpenEntries(context).Select(entry => (entry.Id, entry.Selected)),
            Is.EqualTo(new[] { ("storage", true), ("test", false) }));
        _bar.Select(context, "test");

        Assert.That(commands.Calls, Is.EqualTo(new[] { "store", "switch:test" }));
        Assert.That(_store.Written, Is.EqualTo(new[] { "test" }));
        Assert.That(_bar.OpenSelection, Is.Null);
    }

    [Test]
    public void TerminalSelection_ChoosingTheShownViewOnlyCloses()
    {
        _testAvailable = true;
        Freeze();
        var commands = new RecordingCommands();
        _bar.Press(Context(commands: commands), TerminalSelectionButton.ButtonId, false);

        _bar.Select(Context(commands: commands), "storage");

        Assert.That(commands.Calls, Is.Empty);
        Assert.That(_store.Written, Is.Empty);
        Assert.That(_bar.OpenSelection, Is.Null);
    }

    [Test]
    public void TerminalSelection_IconIsTheShownViewWithCurrentTerminalTooltip()
    {
        _testAvailable = true;
        Freeze();
        var appearance = _bar.GetButtons(Context("test")).Single(b => b.Id == TerminalSelectionButton.ButtonId).Appearance;

        Assert.That(appearance.IconKey, Is.EqualTo("button_placeholder"));
        Assert.That(appearance.TooltipKey, Is.EqualTo(TerminalSelectionButton.TooltipKey));
        Assert.That(appearance.TooltipArgKey, Is.EqualTo("test_name"));
    }

    [TestCase("storage", "button_storage_view", "storage_name")]
    [TestCase("custom", "button_custom_view", "custom_name")]
    public void TerminalSelection_UsesExplicitViewIconsInTriggerAndEntries(string current, string iconKey, string nameKey)
    {
        var registry = Registry(log: _log.Add);
        registry.AddTerminal(new TerminalDefinition("storage", 0, "storage_name", "button_storage_view", _ => true,
            _ => new object()));
        registry.AddTerminal(new TerminalDefinition("custom", 1, "custom_name", "button_custom_view", _ => true,
            _ => new object()));
        registry.Freeze();
        var bar = new TerminalDocks(registry,
            new UniversalTerminalHandler(registry, new MemoryStore(), _log.Add), _log.Add).Find(DockSide.Left)!;
        var context = Context(current);

        var trigger = bar.GetButtons(context).Single(button => button.Id == TerminalSelectionButton.ButtonId).Appearance;
        Assert.That(trigger.IconKey, Is.EqualTo(iconKey));
        Assert.That(trigger.TooltipKey, Is.EqualTo(TerminalSelectionButton.TooltipKey));
        Assert.That(trigger.TooltipArgKey, Is.EqualTo(nameKey));
        bar.Press(context, TerminalSelectionButton.ButtonId, false);
        var entries = bar.GetOpenEntries(context);
        Assert.That(entries.Select(entry => (entry.Id, entry.Appearance.IconKey, entry.Appearance.TooltipKey)),
            Is.EqualTo(new[]
            {
                ("storage", "button_storage_view", "storage_name"),
                ("custom", "button_custom_view", "custom_name")
            }));
        Assert.That(entries.Single(entry => entry.Selected).Id, Is.EqualTo(current));
    }

    [Test]
    public void OpenPanel_ClosesWhenItsButtonDisappears()
    {
        _testAvailable = true;
        Freeze();
        _bar.Press(Context(), TerminalSelectionButton.ButtonId, false);

        _testAvailable = false;
        _bar.GetButtons(Context());

        Assert.That(_bar.OpenSelection, Is.Null);
    }

    [Test]
    public void EachDock_ShowsOnlyItsOwnSideAndTitle()
    {
        _registry.AddAction(Action("left"));
        _registry.AddAction(Action("right", side: DockSide.Right));
        Freeze();

        Assert.That(Ids(Context()), Is.EqualTo(new[] { "left" }));
        Assert.That(RightIds(Context()), Is.EqualTo(new[] { "right" }));
        Assert.That(_bar.Dock.TitleKey, Is.EqualTo("left_title"));
        Assert.That(_right.Dock.TitleKey, Is.EqualTo("right_title"));
        Assert.That(_right.TerminalSelection, Is.Null, "Only the left dock shows the current terminal.");
    }

    [Test]
    public void RightDock_CollapsesWithoutVisibleButtons()
    {
        var shown = false;
        _registry.AddAction(Action("maybe", visible: _ => shown, side: DockSide.Right));
        Freeze();

        Assert.That(RightIds(Context()), Is.Empty);
        shown = true;
        Assert.That(RightIds(Context()), Is.EqualTo(new[] { "maybe" }));
    }

    [Test]
    public void RightDock_HasItsOwnPickerButton()
    {
        _registry.AddAction(Action("lp", placement: ActionPlacement.Picker));
        _registry.AddAction(Action("rp", placement: ActionPlacement.Picker, side: DockSide.Right));
        Freeze();

        Assert.That(Ids(Context()), Is.EqualTo(new[] { ActionSelectionButton.ButtonId }));
        Assert.That(RightIds(Context()), Is.EqualTo(new[] { ActionSelectionButton.IdFor(DockSide.Right) }));
        Assert.That(_right.GetPickerButtons(Context()).Single().Id, Is.EqualTo("rp"));
    }

    [Test]
    public void SelectionPanels_ExcludeEachOtherAcrossDocks()
    {
        _registry.AddAction(Action("lp", placement: ActionPlacement.Picker));
        _registry.AddAction(Action("rp", placement: ActionPlacement.Picker, side: DockSide.Right));
        Freeze();

        _bar.Press(Context(), ActionSelectionButton.ButtonId, false);
        _right.Press(Context(), ActionSelectionButton.IdFor(DockSide.Right), false);

        Assert.That(_bar.OpenSelection, Is.Null);
        Assert.That(_right.OpenSelection, Is.SameAs(_right.ActionSelection));
        Assert.That(_docks.OpenSelection, Is.SameAs(_right.ActionSelection));
    }

    [Test]
    public void SubScreen_ShowsOnlyButtonsOfThatScreen()
    {
        _registry.AddAction(Action("modules"));
        _registry.AddAction(Action("sort", scope: ActionScope.View("storage")));
        _registry.AddAction(Action("apply", scope: ActionScope.Screen("editor"), side: DockSide.Right));
        _registry.AddAction(Action("other", scope: ActionScope.Screen("list"), side: DockSide.Right));
        _testAvailable = true;
        Freeze();

        Assert.That(Ids(ScreenContext("editor", 2)), Is.Empty,
            "View-level buttons and the current terminal button stay off sub-screens.");
        Assert.That(RightIds(ScreenContext("editor", 2)), Is.EqualTo(new[] { "apply" }));
        Assert.That(RightIds(Context()), Is.Empty);
    }

    [Test]
    public void Press_FindsOnlyButtonsOfItsOwnDock()
    {
        var pressed = 0;
        _registry.AddAction(Action("right", press: _ => pressed++, side: DockSide.Right));
        Freeze();

        _bar.Press(Context(), "right", false);
        Assert.That(pressed, Is.Zero);
        _right.Press(Context(), "right", false);
        Assert.That(pressed, Is.EqualTo(1));
    }

    private void Freeze()
    {
        _registry.Freeze();
        _handler = new UniversalTerminalHandler(_registry, _store, _log.Add);
        _docks = new TerminalDocks(_registry, _handler, _log.Add);
        _bar = _docks.Find(DockSide.Left)!;
        _right = _docks.Find(DockSide.Right)!;
    }

    private IEnumerable<string> RightIds(TerminalContext context) =>
        _right.GetButtons(context).Select(button => button.Id).ToArray();

    private IEnumerable<string> Ids(TerminalContext context) => _bar.GetButtons(context).Select(button => button.Id).ToArray();
}
