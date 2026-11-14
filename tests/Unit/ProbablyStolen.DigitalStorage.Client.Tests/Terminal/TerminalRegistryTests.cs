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
        Assert.That(registry.AddSetting(ShadeSetting("x"), 0, ActionPlacement.Rail, null), Is.False);

        Assert.That(registry.Terminals, Has.Count.EqualTo(1));
        Assert.That(registry.Buttons, Has.Count.EqualTo(1));
        Assert.That(_log, Has.Count.EqualTo(3));
        Assert.That(_log, Has.All.Contains("duplicate"));
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
        registry.AddSetting(ShadeSetting(), 3, ActionPlacement.Picker, "storage");
        var entry = registry.Buttons.Single();

        Assert.That(entry.Button, Is.InstanceOf<SettingToggleButton<Shade>>());
        Assert.That(entry.Button.Id, Is.EqualTo("shade"));
        Assert.That(entry.Order, Is.EqualTo(3));
        Assert.That(entry.Placement, Is.EqualTo(ActionPlacement.Picker));
        Assert.That(entry.TerminalId, Is.EqualTo("storage"));
    }

    private TerminalRegistry New() => new(new MemoryConfig(), _log.Add);
}
