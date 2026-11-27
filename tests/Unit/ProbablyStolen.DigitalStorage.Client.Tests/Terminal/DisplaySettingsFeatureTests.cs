using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Client;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.DisplaySettings;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

public sealed class DisplaySettingsFeatureTests
{
    [Test]
    public void Register_AddsASharedRailButtonThatOpensThePageForTheWindow()
    {
        var registry = Registry();
        registry.AddTerminal(Storage());
        object? opener = null;
        DisplaySettingsFeature.Register(registry, 12, window =>
        {
            opener = window;
            return new TestScreen(DisplaySettingsFeature.ScreenId);
        });
        registry.Freeze();

        var entry = registry.Buttons.Single(item => item.Button.Id == DisplaySettingsFeature.ActionId);
        Assert.That(entry.Scope, Is.EqualTo(ActionScope.AllViews));
        Assert.That(entry.Placement, Is.EqualTo(ActionPlacement.Rail));
        Assert.That(entry.Side, Is.EqualTo(DockSide.Left));
        var appearance = entry.Button.GetAppearance(Context());
        Assert.That((appearance.IconKey, appearance.TooltipKey),
            Is.EqualTo(("button_settings", "terminal_button_display_settings_tip")));

        var commands = new RecordingCommands();
        entry.Button.OnPress(Context(commands: commands), backwards: false);

        Assert.That(commands.Calls, Is.EqualTo(new[] { "screen:" + DisplaySettingsFeature.ScreenId }));
        Assert.That(opener, Is.SameAs(Window));
    }

    [Test]
    public void Register_PutsThePageBackButtonOnTheLeftDock()
    {
        var registry = Registry();
        registry.AddTerminal(Storage());
        DisplaySettingsFeature.Register(registry, 12, _ => new TestScreen(DisplaySettingsFeature.ScreenId));
        registry.Freeze();

        Assert.That(ScreenBackButton.IsRegistered(registry, DisplaySettingsFeature.ScreenId), Is.True);
        var back = registry.Buttons.Single(item => item.Button.Id == DisplaySettingsFeature.BackActionId);
        Assert.That(back.Scope, Is.EqualTo(ActionScope.Screen(DisplaySettingsFeature.ScreenId)));
        var commands = new RecordingCommands();
        back.Button.OnPress(new TerminalContext(Array.Empty<string>(), false, TerminalRegistry.StorageTerminalName,
            commands, Window, screenId: DisplaySettingsFeature.ScreenId, screenDepth: 1), backwards: false);
        Assert.That(commands.Calls, Is.EqualTo(new[] { "pop" }));
        Assert.That(registry.DockTitleKey(registry.FindDock(DockSide.Left)!, ScreenContext(DisplaySettingsFeature.ScreenId)),
            Is.EqualTo(StorageTerminalFeature.RightDockTitleKey));
    }

    [Test]
    public void Register_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>((Action)(() => DisplaySettingsFeature.Register(null!, 0, _ => new TestScreen("x"))));
        Assert.Throws<ArgumentNullException>((Action)(() => DisplaySettingsFeature.Register(Registry(), 0, null!)));
    }

    [Test]
    public void Choices_ListEveryTierInOrderWithLabelKeys()
    {
        Assert.That(DisplaySettingsFeature.Sizes, Is.EqualTo(Enum.GetValues(typeof(SizeTier))));
        Assert.That(DisplaySettingsFeature.IconMargins, Is.EqualTo(Enum.GetValues(typeof(IconMarginTier))));
        Assert.That(DisplaySettingsFeature.Sizes.Select(DisplaySettingsFeature.SizeLabelKey),
            Is.EqualTo(new[] { "size_tier_default", "size_tier_small", "size_tier_medium", "size_tier_large" }));
        Assert.That(DisplaySettingsFeature.IconMargins.Select(DisplaySettingsFeature.IconMarginLabelKey),
            Is.EqualTo(new[] { "icon_margin_compact", "icon_margin_standard", "icon_margin_loose" }));
    }
}
