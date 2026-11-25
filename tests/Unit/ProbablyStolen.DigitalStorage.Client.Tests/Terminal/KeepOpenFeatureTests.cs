using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.KeepOpen;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

public sealed class KeepOpenFeatureTests
{
    [Test]
    public void Register_UsesSharedPreferenceForRailAppearanceAndFinalBackGuard()
    {
        var registry = Registry();
        registry.AddTerminal(Storage());
        var preference = false;
        KeepOpenFeature.Register(registry, () => preference, value => preference = value);
        registry.Freeze();

        var button = registry.Buttons.Single(entry => entry.Button.Id == KeepOpenFeature.ActionId);
        Assert.That(button.Scope, Is.EqualTo(ActionScope.AllViews));
        Assert.That(button.Placement, Is.EqualTo(ActionPlacement.Rail));
        Assert.That(button.Button.GetAppearance(Context()).IconKey, Is.EqualTo("button_keep_open_off"));
        Assert.That(registry.AllowsFinalBackClose(), Is.True);

        button.Button.OnPress(Context(), backwards: false);
        Assert.That(preference, Is.True);
        Assert.That(button.Button.GetAppearance(Context()).IconKey, Is.EqualTo("button_keep_open_on"));
        Assert.That(registry.AllowsFinalBackClose(), Is.False);

        button.Button.OnPress(Context(), backwards: false);
        Assert.That(preference, Is.False);
        Assert.That(registry.AllowsFinalBackClose(), Is.True);
    }

    [Test]
    public void Register_OnlyAddsTheGuardToTheStorageTerminalRegistry()
    {
        var storageRegistry = Registry();
        storageRegistry.AddTerminal(Storage());
        KeepOpenFeature.Register(storageRegistry, () => true, _ => { });
        storageRegistry.Freeze();

        var partitionRegistry = new TerminalRegistry(new MemoryConfig(), _ => { }, "partition");
        partitionRegistry.AddTerminal(View("partition"));
        partitionRegistry.Freeze();

        Assert.That(storageRegistry.AllowsFinalBackClose(), Is.False);
        Assert.That(partitionRegistry.AllowsFinalBackClose(), Is.True);
        Assert.That(partitionRegistry.Buttons, Is.Empty);
    }

    [Test]
    public void Register_RejectsInvalidDependencies()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _ => { });
        Assert.Throws<ArgumentNullException>(new Action(() => KeepOpenFeature.Register(registry, null!, _ => { })));
        Assert.Throws<ArgumentNullException>(new Action(() => KeepOpenFeature.Register(registry, () => false, null!)));
    }
}
