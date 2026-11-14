using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Diagnostics;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Partition;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.TerminalStyle;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Client.Workbench;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
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
    public void PartitionFeature_RegistersModeSizeAndItsOwnClearButton()
    {
        var registry = PartitionTerminalFeature.CreateRegistry(new MemoryConfig(), _log.Add, _ => "partition-view",
            _ => PartitionMode.Whitelist, _ => { }, _ => { });
        var bar = new VerticalButtonBar(registry, new UniversalTerminalHandler(registry, new MemoryStore(), _log.Add), _log.Add);

        Assert.That(registry.Terminals.Select(item => item.TerminalName), Is.EqualTo(new[] { "partition" }));
        Assert.That(bar.GetButtons(Context("partition")).Select(item => item.Id), Is.EqualTo(new[]
        {
            PartitionTerminalFeature.ModeActionName, PartitionTerminalFeature.SizeSettingName, PartitionTerminalFeature.ClearActionName
        }));
        Assert.That(registry.Buttons.Select(item => item.TerminalId),
            Is.EqualTo(new string?[] { null, null, PartitionTerminalFeature.TerminalName }));
        Assert.That(_bar.GetButtons(Context()).Select(item => item.Id), Does.Not.Contain(PartitionTerminalFeature.ClearActionName));
    }

    [Test]
    public void PartitionModeAction_UsesTheCurrentWindowDraft()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add, PartitionTerminalFeature.TerminalName);
        var modes = new Dictionary<object, Common.Domain.Storage.Partition.PartitionMode>();
        PartitionTerminalFeature.Register(registry, _ => "partition-view",
            context => modes[(object)context.Window], context =>
            {
                var window = (object)context.Window;
                modes[window] = modes[window] == Common.Domain.Storage.Partition.PartitionMode.Whitelist
                    ? Common.Domain.Storage.Partition.PartitionMode.Blacklist
                    : Common.Domain.Storage.Partition.PartitionMode.Whitelist;
            }, _ => { });
        registry.Freeze();
        var firstWindow = new object();
        var secondWindow = new object();
        modes[firstWindow] = Common.Domain.Storage.Partition.PartitionMode.Whitelist;
        modes[secondWindow] = Common.Domain.Storage.Partition.PartitionMode.Blacklist;
        var action = ((ActionButton)registry.Buttons[0].Button).Action;
        var commands = new RecordingCommands();
        var first = new TerminalContext(Array.Empty<string>(), false, "partition", commands, firstWindow);
        var second = new TerminalContext(Array.Empty<string>(), false, "partition", commands, secondWindow);

        action.OnPress(first);

        Assert.That(modes[firstWindow], Is.EqualTo(Common.Domain.Storage.Partition.PartitionMode.Blacklist));
        Assert.That(modes[secondWindow], Is.EqualTo(Common.Domain.Storage.Partition.PartitionMode.Blacklist));
    }

    [TestCase(PartitionMode.Whitelist)]
    [TestCase(PartitionMode.Blacklist)]
    public void PartitionClearAction_ClearsOnlyTheCurrentWindowAndPreservesItsMode(PartitionMode mode)
    {
        var firstWindow = new object();
        var secondWindow = new object();
        var firstDraft = new PartitionDraft(new PartitionConfig(mode, Array.Empty<PartitionRule>()), 8, 4,
            () => Guid.NewGuid().ToString("N"));
        var secondDraft = new PartitionDraft(new PartitionConfig(mode, Array.Empty<PartitionRule>()), 8, 4,
            () => Guid.NewGuid().ToString("N"));
        firstDraft.AddSample(new ItemKey("item.apple"), 0);
        firstDraft.AddSample(new ItemKey("item.pear"), 7);
        secondDraft.AddSample(new ItemKey("item.plum"), 0);
        var firstChanges = 0;
        var secondChanges = 0;
        firstDraft.Changed += () => firstChanges++;
        secondDraft.Changed += () => secondChanges++;
        var drafts = new Dictionary<object, PartitionDraft> { [firstWindow] = firstDraft, [secondWindow] = secondDraft };
        var registry = PartitionTerminalFeature.CreateRegistry(new MemoryConfig(), _log.Add, _ => "partition-view",
            context => drafts[(object)context.Window].Mode,
            context => drafts[(object)context.Window].ToggleMode(),
            context => drafts[(object)context.Window].Clear());
        var bar = new VerticalButtonBar(registry, new UniversalTerminalHandler(registry, new MemoryStore(), _log.Add), _log.Add);
        var context = new TerminalContext(Array.Empty<string>(), false, "partition", new RecordingCommands(), firstWindow);

        bar.Press(context, PartitionTerminalFeature.ClearActionName, false);
        bar.Press(context, PartitionTerminalFeature.ClearActionName, false);

        Assert.That(firstDraft.Rules, Is.Empty);
        Assert.That(firstDraft.Mode, Is.EqualTo(mode));
        Assert.That(firstChanges, Is.EqualTo(1), "Clearing an empty partition does not notify another save.");
        Assert.That(secondDraft.Rules, Has.Count.EqualTo(1));
        Assert.That(secondDraft.Mode, Is.EqualTo(mode));
        Assert.That(secondChanges, Is.Zero);
        Assert.That(_log, Is.Empty);
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
