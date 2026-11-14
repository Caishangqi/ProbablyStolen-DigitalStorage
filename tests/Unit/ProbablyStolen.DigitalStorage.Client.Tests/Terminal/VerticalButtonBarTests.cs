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
    private VerticalButtonBar _bar = null!;
    private bool _testAvailable;

    [SetUp]
    public void Build()
    {
        _log.Clear();
        _testAvailable = false;
        _store = new MemoryStore();
        _registry = new TerminalRegistry(new MemoryConfig(), _log.Add);
        _registry.AddTerminal(Storage());
        _registry.AddTerminal(View("test", 1, _ => _testAvailable));
    }

    [Test]
    public void StorageView_ShowsSharedButtonsThenItsOwnByOrderThenId()
    {
        _registry.AddAction(Action("modules", 0));
        _registry.AddSetting(ShadeSetting("size"), 10, ActionPlacement.Rail, null);
        _registry.AddSetting(ShadeSetting("sort"), 0, ActionPlacement.Rail, "storage");
        _registry.AddSetting(ShadeSetting("direction"), 10, ActionPlacement.Rail, "storage");
        _registry.AddAction(Action("test.rail", 0, terminalId: "test"));
        Freeze();

        Assert.That(Ids(Context()), Is.EqualTo(new[] { "modules", "size", "sort", "direction" }));
        Assert.That(Ids(Context("test")), Is.EqualTo(new[] { "modules", "size", "test.rail" }));
    }

    [Test]
    public void SharedRailActions_SitBetweenFixedAndViewButtons()
    {
        _registry.AddAction(Action("b-shared", 5));
        _registry.AddAction(Action("a-shared", 5));
        _registry.AddAction(Action("own", -100, terminalId: "storage"));
        Freeze();

        Assert.That(Ids(Context()), Is.EqualTo(new[] { "a-shared", "b-shared", "own" }));
    }

    [Test]
    public void ActionSelection_AppearsOnlyWithAPickerButtonForTheShownView()
    {
        _registry.AddAction(Action("other.picker", placement: ActionPlacement.Picker, terminalId: "test"));
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
        _registry.AddAction(new TerminalAction("ugly", 0, ActionPlacement.Rail, null, _ => true,
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
        Assert.That(_bar.TerminalSelection.MenuOpen, Is.True);

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
    public void TerminalSelection_LabelIsTheShownViewWithCurrentTerminalTooltip()
    {
        _testAvailable = true;
        Freeze();
        var appearance = _bar.GetButtons(Context("test")).Single(b => b.Id == TerminalSelectionButton.ButtonId).Appearance;

        Assert.That(appearance.LabelKey, Is.EqualTo("test_name"));
        Assert.That(appearance.TooltipKey, Is.EqualTo(TerminalSelectionButton.TooltipKey));
        Assert.That(appearance.TooltipArgKey, Is.EqualTo("test_name"));
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

    private void Freeze()
    {
        _registry.Freeze();
        _handler = new UniversalTerminalHandler(_registry, _store, _log.Add);
        _bar = new VerticalButtonBar(_registry, _handler, _log.Add);
    }

    private IEnumerable<string> Ids(TerminalContext context) => _bar.GetButtons(context).Select(button => button.Id).ToArray();
}
