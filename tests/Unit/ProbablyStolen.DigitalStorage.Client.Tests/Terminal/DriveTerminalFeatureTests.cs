using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Drive;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class DriveTerminalFeatureTests
{
    [Test]
    public void Registry_ShowsModulesAndPriorityOnTheViewAndOnlyBackOnThePriorityPage()
    {
        var registry = DriveTerminalFeature.CreateRegistry(new MemoryConfig(), _ => { }, _ => new object(),
            _ => new TestScreen("modules"), _ => new TestScreen(DriveTerminalFeature.PriorityScreenId));
        var left = new TerminalDocks(registry, new UniversalTerminalHandler(registry, new MemoryStore(), _ => { }), _ => { })
            .Find(DockSide.Left)!;
        var overView = TerminalFixtures.Context(DriveTerminalFeature.TerminalName);
        var onPriority = TerminalFixtures.ScreenContext(DriveTerminalFeature.PriorityScreenId,
            current: DriveTerminalFeature.TerminalName);

        Assert.That(registry.DefaultTerminal.TerminalName, Is.EqualTo(DriveTerminalFeature.TerminalName));
        Assert.That(left.GetButtons(overView).Select(button => button.Id),
            Is.EqualTo(new[] { ModulesFeature.ActionId, DriveTerminalFeature.PriorityActionId }));
        Assert.That(left.TitleKey(overView), Is.EqualTo(StorageTerminalFeature.LeftDockTitleKey));
        Assert.That(left.GetButtons(onPriority).Select(button => button.Id),
            Is.EqualTo(new[] { DriveTerminalFeature.PriorityBackActionId }));
        Assert.That(left.TitleKey(onPriority), Is.EqualTo(StorageTerminalFeature.RightDockTitleKey));
        Assert.That(registry.FindDock(DockSide.Right), Is.Null, "The drive window reserves no right dock.");
        var onModules = TerminalFixtures.ScreenContext(ModulesFeature.ScreenId, current: DriveTerminalFeature.TerminalName);
        Assert.That(left.GetButtons(onModules).Select(button => button.Id),
            Is.EqualTo(new[] { DriveTerminalFeature.ModulesBackActionId }));
        Assert.That(ScreenBackButton.IsRegistered(registry, ModulesFeature.ScreenId), Is.True);
        Assert.That(ScreenBackButton.IsRegistered(registry, "other"), Is.False);
    }

    [Test]
    public void PriorityButton_OpensThePriorityPageAndBackReturns()
    {
        var registry = DriveTerminalFeature.CreateRegistry(new MemoryConfig(), _ => { }, _ => new object(),
            _ => new TestScreen("modules"), _ => new TestScreen(DriveTerminalFeature.PriorityScreenId));
        var left = new TerminalDocks(registry, new UniversalTerminalHandler(registry, new MemoryStore(), _ => { }), _ => { })
            .Find(DockSide.Left)!;
        var commands = new RecordingCommands();

        left.Press(TerminalFixtures.Context(DriveTerminalFeature.TerminalName, commands: commands),
            DriveTerminalFeature.PriorityActionId, false);
        var onPriority = new TerminalContext(System.Array.Empty<string>(), false, DriveTerminalFeature.TerminalName, commands,
            TerminalFixtures.Window, screenId: DriveTerminalFeature.PriorityScreenId, screenDepth: 1);
        left.Press(onPriority, DriveTerminalFeature.PriorityBackActionId, false);

        Assert.That(commands.Calls, Is.EqualTo(new[] { "screen:" + DriveTerminalFeature.PriorityScreenId, "pop" }));
    }
}
