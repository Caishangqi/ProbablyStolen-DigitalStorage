using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Common.Tests.Terminal.Stacking;

[TestFixture]
public sealed class StackingConfigCodecTests
{
    [Test]
    public void RoundTrip_KeepsSwitchRowsAndOrder()
    {
        var config = new StackingConfig(false, new[]
        {
            new StackingRule("price_composition", StackingMode.Ignore),
            new StackingRule("feature:光泽|x=", StackingMode.Match),
            new StackingRule("tag:AGRI_PERFUME_RECORDED", StackingMode.Ignore)
        });
        var decoded = StackingConfigCodec.Decode(StackingConfigCodec.Encode(config));
        Assert.That(decoded.Status, Is.EqualTo(StackingConfigStatus.Ok));
        Assert.That(decoded.Config, Is.EqualTo(config));
    }

    [Test]
    public void RoundTrip_KeepsMarker()
    {
        var config = StackingConfig.Default.WithMarker(new StackingMarker(0xB37FEB)).WithAddedRule("a", StackingMode.Ignore);
        var text = StackingConfigCodec.Encode(config);
        Assert.That(text, Does.StartWith("v3\nenabled\t1\nmarker\t#B37FEB\n"));
        Assert.That(StackingConfigCodec.Decode(text).Config, Is.EqualTo(config));
    }

    [TestCase("v1\nenabled\t0\nrule\tc3RvbGVu\tignore")]
    [TestCase("v2\nenabled\t0\nmarker\t#B37FEB\nrule\tc3RvbGVu\tignore")]
    public void ValidLegacyText_ResetsToDefaults(string text)
    {
        var result = StackingConfigCodec.Decode(text);
        Assert.That(result.Status, Is.EqualTo(StackingConfigStatus.Reset));
        Assert.That(result.Config, Is.EqualTo(StackingConfig.Default));
    }

    [TestCase("#4cd964", 0x4CD964)]
    [TestCase("#ABC", 0xAABBCC)]
    [TestCase(" #fff ", 0xFFFFFF)]
    [TestCase("#000000", 0)]
    public void Marker_ParsesShortAndLongForms(string text, int rgb)
    {
        Assert.That(StackingMarker.TryParse(text, out var marker), Is.True);
        Assert.That(marker.Rgb, Is.EqualTo(rgb));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("#")]
    [TestCase("4CD964")]
    [TestCase("#4CD96")]
    [TestCase("#GGGGGG")]
    [TestCase("#4CD9640")]
    public void Marker_RejectsOtherText(string? text)
    {
        Assert.That(StackingMarker.TryParse(text, out var marker), Is.False);
        Assert.That(marker, Is.EqualTo(StackingMarker.Default));
    }

    [Test]
    public void Marker_PresetsStartWithTheDefaultAndAreDistinct()
    {
        Assert.That(StackingMarker.Presets[0], Is.EqualTo(StackingMarker.Default));
        Assert.That(StackingMarker.Presets.Select(marker => marker.Rgb), Is.Unique);
        Assert.That(StackingMarker.Presets.Select(marker => marker.Rgb), Has.Member(0xB37FEB));
    }

    [Test]
    public void Marker_FormatsUpperCaseSixDigits()
    {
        Assert.That(new StackingMarker(0x0A0B0C).Format(), Is.EqualTo("#0A0B0C"));
        Assert.That(StackingMarker.Default.Format(), Is.EqualTo("#4CD964"));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => _ = new StackingMarker(0x1000000)));
    }

    [TestCase(null)]
    [TestCase("")]
    public void MissingText_MeansDefault(string? text)
    {
        var result = StackingConfigCodec.Decode(text);
        Assert.That(result.Status, Is.EqualTo(StackingConfigStatus.Missing));
        Assert.That(result.Config, Is.EqualTo(StackingConfig.Default));
    }

    [TestCase("v4\nenabled\t1", "unknown_version")]
    [TestCase("v3\nenabled\t1", "bad_marker")]
    [TestCase("v2\nenabled\t1", "bad_marker")]
    [TestCase("v2\nenabled\t1\nmarker\t#4cd964", "bad_marker")]
    [TestCase("v2\nenabled\t1\nmarker\t#4CD", "bad_marker")]
    [TestCase("v2\nenabled\t1\nmarker\tgreen", "bad_marker")]
    [TestCase("v2\nenabled\t1\nrule\tcHJpY2U=\tmatch", "bad_marker")]
    [TestCase("v2\nenabled\t1\nmarker\t#4CD964\nrule\tcHJpY2U=", "bad_line")]
    [TestCase("v1", "missing_switch")]
    [TestCase("v1\nenabled\tyes", "bad_switch")]
    [TestCase("v1\nenabled\t1\nrule\tcHJpY2U=", "bad_line")]
    [TestCase("v1\nenabled\t1\nrow\tcHJpY2U=\tmatch", "bad_line")]
    [TestCase("v1\nenabled\t1\nrule\t!!!\tmatch", "bad_field")]
    [TestCase("v1\nenabled\t1\nrule\tcHJpY2U=\tdefault", "bad_mode")]
    [TestCase("v1\nenabled\t1\nrule\tcHJpY2U=\tmatch\nrule\tcHJpY2U=\tignore", "duplicate_field")]
    public void DamagedText_IsUnreadableAndKept(string text, string reason)
    {
        var result = StackingConfigCodec.Decode(text);
        Assert.That(result.Status, Is.EqualTo(StackingConfigStatus.Unreadable));
        Assert.That(result.Config, Is.Null);
        Assert.That(result.Raw, Is.EqualTo(text));
        Assert.That(result.Reason, Is.EqualTo(reason));
    }

    [Test]
    public void OversizedText_IsUnreadable()
    {
        var text = "v1\nenabled\t1" + new string('x', StackingConfigCodec.MaxLength);
        Assert.That(StackingConfigCodec.Decode(text).Status, Is.EqualTo(StackingConfigStatus.Unreadable));
    }

    [Test]
    public void TooManyRules_AreRefused()
    {
        var rules = Enumerable.Range(0, StackingConfig.MaxRules + 1).Select(i => new StackingRule("f" + i, StackingMode.Match));
        Refused(() => _ = new StackingConfig(true, rules));
        var lines = string.Join("\n", Enumerable.Range(0, StackingConfig.MaxRules + 1).Select(i => "rule\tZg==\tmatch"));
        Assert.That(StackingConfigCodec.Decode("v1\nenabled\t1\n" + lines).Reason, Is.EqualTo("too_many_rules"));
    }

    [Test]
    public void Config_RefusesDuplicateAndInvalidFields()
    {
        Refused(() => _ = new StackingConfig(true, new[]
        {
            new StackingRule("a", StackingMode.Match), new StackingRule("a", StackingMode.Ignore)
        }));
        Refused(() => _ = new StackingRule("", StackingMode.Match));
        Refused(() => _ = new StackingRule("a\nb", StackingMode.Match));
        Refused(() => _ = new StackingRule(new string('a', StackingRule.MaxFieldIdLength + 1), StackingMode.Match));
    }

    [Test]
    public void Edits_ReturnNewConfigurations()
    {
        var config = StackingConfig.Default.WithAddedRule("a", StackingMode.Ignore).WithAddedRule("b", StackingMode.Ignore);
        Assert.That(config.WithAddedRule("a", StackingMode.Match), Is.SameAs(config));
        Assert.That(config.WithMode(1, StackingMode.Match).ModeOf("b"), Is.EqualTo(StackingMode.Match));
        Assert.That(config.WithField(0, "c").Rules.Select(rule => rule.FieldId), Is.EqualTo(new[] { "c", "b" }));
        Refused(() => config.WithField(0, "b"));
        Assert.That(config.WithoutRule(0).Rules.Single().FieldId, Is.EqualTo("b"));
        Assert.That(config.WithoutRules().Rules, Is.Empty);
        Assert.That(config.WithEnabled(false).Enabled, Is.False);
        var tinted = config.WithMarker(new StackingMarker(0xFF5C5C));
        Assert.That(tinted, Is.Not.EqualTo(config));
        Assert.That(tinted.WithoutRule(0).WithEnabled(false).WithMode(0, StackingMode.Match).Marker.Rgb, Is.EqualTo(0xFF5C5C));
        Assert.That(StackingConfig.Default.Rules, Is.Empty);
    }

    private static void Refused(Action code) => Assert.Throws<ArgumentException>(code);
}
