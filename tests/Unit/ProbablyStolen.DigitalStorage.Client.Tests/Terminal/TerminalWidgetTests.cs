using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class TerminalWidgetTests
{
    [Test]
    public void Setting_RejectsEmptyOrRepeatedValues()
    {
        Assert.Throws<ArgumentException>(new Action(() => { _ = new Setting<Shade>("s", Array.Empty<Shade>(), _ => Appearance); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = new Setting<Shade>("s", new[] { Shade.Light, Shade.Light }, _ => Appearance); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = new Setting<Shade>("", new[] { Shade.Light }, _ => Appearance); }));
    }

    [Test]
    public void SettingToggle_CyclesForwardAndBackwardWithWrap()
    {
        var config = new MemoryConfig();
        var toggle = new SettingToggleButton<Shade>(ShadeSetting(), config);

        Assert.That(toggle.GetCurrentValue(), Is.EqualTo(Shade.Light));
        Assert.That(toggle.GetNextValue(backwards: false), Is.EqualTo(Shade.Medium));
        Assert.That(toggle.GetNextValue(backwards: true), Is.EqualTo(Shade.Dark));
        toggle.Set(Shade.Dark);
        Assert.That(toggle.GetNextValue(backwards: false), Is.EqualTo(Shade.Light));
    }

    [Test]
    public void SettingToggle_ValueOutsideTheSettingStartsFromTheFirstValue()
    {
        var config = new MemoryConfig();
        var setting = new Setting<Shade>("two", new[] { Shade.Medium, Shade.Dark }, _ => Appearance);
        config.Force("two", Shade.Light);

        Assert.That(new SettingToggleButton<Shade>(setting, config).GetNextValue(false), Is.EqualTo(Shade.Medium));
    }

    [Test]
    public void SettingToggle_PressWritesThenNotifies()
    {
        var config = new MemoryConfig();
        var commands = new RecordingCommands();
        var toggle = new SettingToggleButton<Shade>(ShadeSetting(), config);

        toggle.OnPress(Context(commands: commands), backwards: false);

        Assert.That(config.GetSetting(toggle.Setting), Is.EqualTo(Shade.Medium));
        Assert.That(config.Writes, Is.EqualTo(1));
        Assert.That(commands.Calls, Is.EqualTo(new[] { "setting:shade" }));
        Assert.That(toggle.GetAppearance(Context()).IconKey, Is.EqualTo("shade_medium"));
    }

    [Test]
    public void ActionButton_ForwardsAppearanceAndPress()
    {
        var pressed = 0;
        var button = new ActionButton(Action("go", press: _ => pressed++));

        button.OnPress(Context(), backwards: true);

        Assert.That(button.Id, Is.EqualTo("go"));
        Assert.That(button.GetAppearance(Context()), Is.SameAs(Appearance));
        Assert.That(pressed, Is.EqualTo(1));
    }
}
