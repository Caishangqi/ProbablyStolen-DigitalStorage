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
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class TerminalFeatureTests
{
    private readonly List<string> _log = new();
    private TerminalRegistry _registry = null!;
    private VerticalButtonBar _bar = null!;

    [SetUp]
    public void Build()
    {
        _log.Clear();
        _registry = new TerminalRegistry(new MemoryConfig(), _log.Add);
        StorageTerminalFeature.Register(_registry, window => "storage-view:" + window.GetHashCode());
        ModulesFeature.Register(_registry, window => "modules-screen");
        TerminalStyleFeature.Register(_registry);
        DiagnosticsFeature.Register(_registry, _ => "diagnostics-view", _ => "diagnostics-screen", _log.Add);
        _registry.Freeze();
        _bar = new VerticalButtonBar(_registry, new UniversalTerminalHandler(_registry, new MemoryStore(), _log.Add), _log.Add);
    }

    [Test]
    public void StorageView_RailIsModulesSizeSortDirection()
    {
        Assert.That(_bar.GetButtons(Context()).Select(button => button.Id),
            Is.EqualTo(new[] { "modules", TerminalStyleFeature.SettingName, StorageTerminalFeature.SortByName, StorageTerminalFeature.SortDirectionName }));
        Assert.That(_log, Is.Empty);
    }

    [Test]
    public void DiagnosticsView_IsHiddenUntilTestViewEnabled()
    {
        var off = _bar.GetButtons(Context(testView: false)).Select(button => button.Id).ToArray();
        Assert.That(off, Does.Not.Contain(TerminalSelectionButton.ButtonId));
        Assert.That(off, Does.Not.Contain(ActionSelectionButton.ButtonId));

        var storage = _bar.GetButtons(Context(testView: true)).Select(button => button.Id).ToArray();
        Assert.That(storage.Last(), Is.EqualTo(TerminalSelectionButton.ButtonId));
        Assert.That(storage, Does.Not.Contain(ActionSelectionButton.ButtonId), "Built-in buttons are never pickers.");

        var diagnostics = _bar.GetButtons(Context(DiagnosticsFeature.TerminalName, testView: true))
            .Select(button => button.Id).ToArray();
        Assert.That(diagnostics, Is.EqualTo(new[]
        {
            "modules", TerminalStyleFeature.SettingName, DiagnosticsFeature.RailActionId, ActionSelectionButton.ButtonId,
            TerminalSelectionButton.ButtonId
        }));
    }

    [Test]
    public void ModulesButton_OpensTheScreenForThisWindow()
    {
        var commands = new RecordingCommands();
        _bar.Press(Context(commands: commands), ModulesFeature.ActionId, false);

        Assert.That(commands.Calls, Is.EqualTo(new[] { "screen:modules-screen" }));
    }

    [Test]
    public void DiagnosticsButtons_OnlyLogOrOpenThePlaceholder()
    {
        var commands = new RecordingCommands();
        var context = Context(DiagnosticsFeature.TerminalName, testView: true, commands: commands);

        _bar.Press(context, DiagnosticsFeature.RailActionId, false);
        _bar.Press(context, ActionSelectionButton.ButtonId, false);
        _bar.Select(context, DiagnosticsFeature.PickerActionId);

        Assert.That(_log, Is.EqualTo(new[] { "diagnostics_rail pressed" }));
        Assert.That(commands.Calls, Is.EqualTo(new[] { "screen:diagnostics-screen" }));
    }

    [TestCase(TerminalSortOrder.Name, "terminal_button_sort_name", "terminal_sort_name")]
    [TestCase(TerminalSortOrder.Amount, "terminal_button_sort_amount", "terminal_sort_amount")]
    public void SortAppearance_KeepsTheExistingKeys(TerminalSortOrder order, string label, string argument)
    {
        var appearance = StorageTerminalFeature.SortBy.GetAppearance(order);

        Assert.That(appearance.LabelKey, Is.EqualTo(label));
        Assert.That(appearance.TooltipKey, Is.EqualTo("terminal_button_sort_tip"));
        Assert.That(appearance.TooltipArgKey, Is.EqualTo(argument));
    }

    [Test]
    public void SizeAppearance_KeepsTheExistingKeysAndCyclesLikeBefore()
    {
        Assert.That(TerminalStyleFeature.TerminalStyle.Values,
            Is.EqualTo(new[] { TerminalSize.Small, TerminalSize.Medium, TerminalSize.Tall, TerminalSize.Full }));
        Assert.That(TerminalStyleFeature.TerminalStyle.GetAppearance(TerminalSize.Tall).LabelKey,
            Is.EqualTo("terminal_button_size_tall"));
        Assert.That(StorageTerminalFeature.SortDirection.GetAppearance(TerminalSortDirection.Descending).TooltipArgKey,
            Is.EqualTo("terminal_direction_desc"));
    }

    [Test]
    public void SettingNames_AreTheExistingConfigKeys()
    {
        // The rail and the settings panel must share the values players already have.
        Assert.That(StorageTerminalFeature.SortBy.Name, Is.EqualTo("terminal_view_sort"));
        Assert.That(StorageTerminalFeature.SortDirection.Name, Is.EqualTo("terminal_view_direction"));
        Assert.That(TerminalStyleFeature.TerminalStyle.Name, Is.EqualTo("terminal_view_size"));
    }

    [Test]
    public void StorageView_IsFirstAndAlwaysAvailable()
    {
        var storage = _registry.Terminals.First();

        Assert.That(storage.TerminalName, Is.EqualTo(TerminalRegistry.StorageTerminalName));
        Assert.That(storage.IsAvailable(Context()), Is.True);
        Assert.That(storage.CreateView(Window), Is.EqualTo("storage-view:" + Window.GetHashCode()));
    }
}
