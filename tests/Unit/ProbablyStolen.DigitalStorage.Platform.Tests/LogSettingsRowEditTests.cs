using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
internal sealed class LogSettingsRowEditTests
{
    [Test]
    public void Apply_GroupingFailureRestoresLabelsIndexAndParent()
    {
        var label = "Info";
        var index = 0;
        var parent = "switch_row";
        LogSettingsRowEdit.Apply(() => { label = "Localized"; index = 2; }, () =>
        {
            parent = "verbosity_row";
            throw new InvalidOperationException();
        }, () => { label = "Info"; index = 0; }, () => parent = "switch_row");
        Assert.That((label, index, parent), Is.EqualTo(("Info", 0, "switch_row")));
    }

    [Test]
    public void Apply_GroupingAndLocalizationKeepNativeSelectionAndApplyCallback()
    {
        var choices = new[] { "Error", "Warning", "Info", "Verbose" };
        var displayed = (string[])choices.Clone();
        var index = 2;
        var saved = "Warning";
        var enabled = true;
        var grouped = false;
        Action apply = () => saved = choices[index];
        LogSettingsRowEdit.Apply(() => displayed[2] = "Localized", () => grouped = true,
            () => displayed[2] = choices[2], () => grouped = false);
        Assert.That((grouped, enabled, saved, index), Is.EqualTo((true, true, "Warning", 2)));
        apply();
        Assert.That(saved, Is.EqualTo("Info"));
    }
}
