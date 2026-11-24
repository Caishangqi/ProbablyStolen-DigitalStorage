using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Machines.IoPort;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.IoPort;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class IoPortPanelStateTests
{
    [TestCase(IoPortDirection.Forward, true, "io_direction_cell_to_network")]
    [TestCase(IoPortDirection.Reverse, false, "io_direction_network_to_cell")]
    public void From_DirectionSetsArrowAndText(IoPortDirection direction, bool right, string key)
    {
        var state = IoPortPanelState.From(Snapshot(new IoPortSettings(direction, IoPortCompletionMode.Full, true)));

        Assert.That(state.SettingsKnown, Is.True);
        Assert.That(state.ArrowPointsRight, Is.EqualTo(right));
        Assert.That(state.DirectionTextKey, Is.EqualTo(key));
        Assert.That(state.CompletionIconKey, Is.EqualTo("button_io_finish_full"));
        Assert.That(state.CompletionTextKey, Is.EqualTo("io_finish_full"));
    }

    [Test]
    public void From_PausedAndUnknownSettings()
    {
        var paused = IoPortPanelState.From(new IoPortDisplaySnapshot(IoPortRuntimeStatus.Paused,
            IoPortSettings.Default.WithRunning(false), IoPortProgress.FromRoots(1, 3)));
        var unknown = IoPortPanelState.From(IoPortDisplaySnapshot.SettingsUnavailable);

        Assert.That((paused.Running, paused.StatusTextKey, paused.Progress), Is.EqualTo((false, "io_status_paused", (double?)0.25d)));
        Assert.That(unknown.SettingsKnown, Is.False);
        Assert.That(unknown.StatusTextKey, Is.EqualTo("io_status_settings_unavailable"));
        Assert.That(unknown.DirectionTextKey, Is.Null);
        Assert.That(unknown.Progress, Is.Null);
    }

    [Test]
    public void From_EveryStatusHasText()
    {
        foreach (IoPortRuntimeStatus status in Enum.GetValues(typeof(IoPortRuntimeStatus)))
            Assert.That(IoPortPanelState.From(new IoPortDisplaySnapshot(status, IoPortSettings.Default, IoPortProgress.Unknown))
                .StatusTextKey, Does.StartWith("io_status_"));
    }

    [Test]
    public void NextCompletion_CyclesThroughEveryMode()
    {
        var seen = Enumerable.Range(0, 3).Aggregate((Mode: IoPortCompletionMode.WorkComplete, List: new IoPortCompletionMode[0]),
            (acc, _) => (IoPortPanelState.NextCompletion(acc.Mode), acc.List.Append(acc.Mode).ToArray())).List;

        Assert.That(seen, Is.EquivalentTo(Enum.GetValues(typeof(IoPortCompletionMode))));
        Assert.That(IoPortPanelState.NextCompletion(IoPortCompletionMode.Full), Is.EqualTo(IoPortCompletionMode.WorkComplete));
    }

    [Test]
    public void CreateRegistry_RegistersModulesBeforeCompletionAction()
    {
        var mode = (IoPortCompletionMode?)IoPortCompletionMode.WorkComplete;
        var registry = IoPortTerminalFeature.CreateRegistry(new MemoryConfig(), _ => { }, _ => new object(), _ => new object(),
            _ => mode, (_, next) => mode = next);

        var rail = new VerticalButtonBar(registry,
            new UniversalTerminalHandler(registry, new MemoryStore(), _ => { }), _ => { });
        var context = TerminalFixtures.Context(IoPortTerminalFeature.TerminalName);

        Assert.That(registry.DefaultTerminal.TerminalName, Is.EqualTo(IoPortTerminalFeature.TerminalName));
        Assert.That(registry.Buttons.Select(button => button.Button.Id),
            Is.EqualTo(new[] { ModulesFeature.ActionId, IoPortTerminalFeature.CompletionActionId }));
        Assert.That(rail.GetButtons(context).Select(button => button.Id),
            Is.EqualTo(new[] { ModulesFeature.ActionId, IoPortTerminalFeature.CompletionActionId }));
    }

    private static IoPortDisplaySnapshot Snapshot(IoPortSettings settings) =>
        new(IoPortRuntimeStatus.Working, settings, IoPortProgress.Unknown);
}
