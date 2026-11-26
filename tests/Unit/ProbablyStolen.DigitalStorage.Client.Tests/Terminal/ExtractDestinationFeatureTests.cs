using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.ExtractDestination;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

public sealed class ExtractDestinationFeatureTests
{
    [Test]
    public void For_PutsThePreferredDestinationFirstAndKeepsTheOther()
    {
        Assert.That(ExtractionOrder.For(ExtractionPriority.WarehouseFirst),
            Is.EqualTo(new[] { ExtractionDestination.Warehouse, ExtractionDestination.Counter }));
        Assert.That(ExtractionOrder.For(ExtractionPriority.CounterFirst),
            Is.EqualTo(new[] { ExtractionDestination.Counter, ExtractionDestination.Warehouse }));
        Assert.That(ExtractionOrder.For((ExtractionPriority)42),
            Is.EqualTo(new[] { ExtractionDestination.Warehouse, ExtractionDestination.Counter }));
    }

    [Test]
    public void Next_SwitchesBetweenBothPriorities()
    {
        Assert.That(ExtractionOrder.Next(ExtractionPriority.WarehouseFirst), Is.EqualTo(ExtractionPriority.CounterFirst));
        Assert.That(ExtractionOrder.Next(ExtractionPriority.CounterFirst), Is.EqualTo(ExtractionPriority.WarehouseFirst));
    }

    [Test]
    public void Register_AddsAStorageRailButtonThatSwitchesTheSharedPriority()
    {
        var registry = Registry();
        registry.AddTerminal(Storage());
        var priority = ExtractionPriority.WarehouseFirst;
        ExtractDestinationFeature.Register(registry, () => priority, value => priority = value);
        registry.Freeze();

        var button = registry.Buttons.Single(entry => entry.Button.Id == ExtractDestinationFeature.ActionId);
        Assert.That(button.Scope, Is.EqualTo(ActionScope.View(TerminalRegistry.StorageTerminalName)));
        Assert.That(button.Placement, Is.EqualTo(ActionPlacement.Rail));
        var appearance = button.Button.GetAppearance(Context());
        Assert.That((appearance.IconKey, appearance.TooltipKey, appearance.TooltipArgKey),
            Is.EqualTo(("button_extract_warehouse", ExtractDestinationFeature.TooltipKey, "terminal_extract_warehouse_first")));

        button.Button.OnPress(Context(), backwards: false);
        Assert.That(priority, Is.EqualTo(ExtractionPriority.CounterFirst));
        Assert.That(button.Button.GetAppearance(Context()).IconKey, Is.EqualTo("button_extract_counter"));

        button.Button.OnPress(Context(), backwards: true);
        Assert.That(priority, Is.EqualTo(ExtractionPriority.WarehouseFirst));
    }

    [Test]
    public void Register_RejectsInvalidDependencies()
    {
        var registry = Registry();
        Assert.Throws<ArgumentNullException>(new Action(() =>
            ExtractDestinationFeature.Register(null!, () => ExtractionPriority.WarehouseFirst, _ => { })));
        Assert.Throws<ArgumentNullException>(new Action(() => ExtractDestinationFeature.Register(registry, null!, _ => { })));
        Assert.Throws<ArgumentNullException>(new Action(() =>
            ExtractDestinationFeature.Register(registry, () => ExtractionPriority.WarehouseFirst, null!)));
    }
}
