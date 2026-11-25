using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Common.Tests.Terminal.Stacking;

[TestFixture]
public sealed class StackingConfigCodecTests
{
    [Test]
    public void RoundTrip_KeepsItemsSwitchesRowsAndOrder()
    {
        var config = new StackingConfig(new[]
        {
            new StackingItemRule("cabbage", false, new[]
            {
                new StackingRule("price_composition", StackingMode.Ignore),
                new StackingRule("feature:光泽|x=", StackingMode.Match)
            }),
            new StackingItemRule("模组:物品", true, new[] { new StackingRule("tag:AGRI_PERFUME_RECORDED", StackingMode.Ignore) }),
            new StackingItemRule("empty", true, Array.Empty<StackingRule>())
        }, new StackingMarker(0xB37FEB), advanced: true);
        var decoded = StackingConfigCodec.Decode(StackingConfigCodec.Encode(config));
        Assert.That(decoded.Status, Is.EqualTo(StackingConfigStatus.Ok));
        Assert.That(decoded.Config, Is.EqualTo(config));
    }

    [Test]
    public void Encode_WritesV5WithMarkerAndAdvancedFirst()
    {
        var config = StackingConfig.Default.WithMarker(new StackingMarker(0xB37FEB))
            .WithItem(new StackingItemRule("a", true, new[] { new StackingRule("b", StackingMode.Ignore) }));
        Assert.That(StackingConfigCodec.Encode(config), Is.EqualTo("v5\nmarker\t#B37FEB\nadvanced\t1\nitem\tYQ==\t1\nrule\tYg==\tignore"));
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

    [TestCase("v6\nmarker\t#4CD964\nadvanced\t0", "unknown_version")]
    [TestCase("v03\nmarker\t#4CD964", "unknown_version")]
    [TestCase("v5", "bad_marker")]
    [TestCase("v5\nmarker\t#4cd964\nadvanced\t0", "bad_marker")]
    [TestCase("v5\nmarker\t#4CD964", "bad_advanced")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\tyes", "bad_advanced")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nrule\tcHJpY2U=\tmatch", "rule_without_item")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==", "bad_line")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\t!!!\t1", "bad_item")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==\tyes", "bad_switch")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==\t1\nitem\tYQ==\t0", "duplicate_item")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==\t1\nrule\tcHJpY2U=\tdefault", "bad_mode")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==\t1\nrule\t!!!\tmatch", "bad_field")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==\t1\nrule\tcHJpY2U=\tmatch\nrule\tcHJpY2U=\tignore", "duplicate_field")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==\t1\nrow\tcHJpY2U=\tmatch", "bad_line")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nglobal\tcHJpY2U=\tignore", "bad_line")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nglobal\tcHJpY2U=\tignore\tyes", "bad_switch")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nglobal\tcHJpY2U=\tdefault\t1", "bad_mode")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nglobal\t!!!\tignore\t1", "bad_field")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nglobal\tcHJpY2U=\tignore\t1\nglobal\tcHJpY2U=\tmatch\t0", "duplicate_global")]
    [TestCase("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==\t1\nglobal\tcHJpY2U=\tignore\t1", "bad_line")]
    public void DamagedText_IsUnreadableAndKept(string text, string reason)
    {
        var result = StackingConfigCodec.Decode(text);
        Assert.That(result.Status, Is.EqualTo(StackingConfigStatus.Unreadable));
        Assert.That(result.Config, Is.Null);
        Assert.That(result.Raw, Is.EqualTo(text));
        Assert.That(result.Reason, Is.EqualTo(reason));
    }

    [Test]
    public void OversizedText_IsUnreadableAndOversizedConfigurationsAreNotEncoded()
    {
        var text = "v5\nmarker\t#4CD964\nadvanced\t0" + new string('x', StackingConfigCodec.MaxLength);
        Assert.That(StackingConfigCodec.Decode(text).Status, Is.EqualTo(StackingConfigStatus.Unreadable));
        var rows = Enumerable.Range(0, StackingItemRule.MaxRules)
            .Select(i => new StackingRule(new string('f', 150) + i, StackingMode.Ignore));
        var large = new StackingConfig(new[] { new StackingItemRule("a", true, rows) });
        Refused(() => StackingConfigCodec.Encode(large));
    }

    [Test]
    public void TooManyRowsOrItems_AreRefused()
    {
        var rules = Enumerable.Range(0, StackingItemRule.MaxRules + 1).Select(i => new StackingRule("f" + i, StackingMode.Match));
        Refused(() => _ = new StackingItemRule("a", true, rules));
        var lines = string.Join("\n", Enumerable.Range(0, StackingItemRule.MaxRules + 1)
            .Select(i => "rule\t" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("f" + i)) + "\tmatch"));
        Assert.That(StackingConfigCodec.Decode("v5\nmarker\t#4CD964\nadvanced\t0\nitem\tYQ==\t1\n" + lines).Reason,
            Is.EqualTo("too_many_rules"));
        var items = Enumerable.Range(0, StackingConfig.MaxItems + 1)
            .Select(i => new StackingItemRule("i" + i, true, Array.Empty<StackingRule>()));
        Refused(() => _ = new StackingConfig(items));
    }

    [Test]
    public void Config_RefusesDuplicateItemsFieldsAndInvalidIdentifiers()
    {
        Refused(() => _ = new StackingItemRule("a", true, new[]
        {
            new StackingRule("a", StackingMode.Match), new StackingRule("a", StackingMode.Ignore)
        }));
        Refused(() => _ = new StackingConfig(new[]
        {
            new StackingItemRule("a", true, Array.Empty<StackingRule>()),
            new StackingItemRule("a", false, Array.Empty<StackingRule>())
        }));
        Refused(() => _ = new StackingItemRule("", true, Array.Empty<StackingRule>()));
        Refused(() => _ = new StackingItemRule("a\nb", true, Array.Empty<StackingRule>()));
        Refused(() => _ = new StackingRule("", StackingMode.Match));
        Refused(() => _ = new StackingRule("a\nb", StackingMode.Match));
        Refused(() => _ = new StackingRule(new string('a', StackingRule.MaxFieldIdLength + 1), StackingMode.Match));
    }

    [Test]
    public void Edits_ReturnNewConfigurations()
    {
        var a = new StackingItemRule("a", true, new[] { new StackingRule("x", StackingMode.Match) });
        var b = new StackingItemRule("b", true, Array.Empty<StackingRule>());
        var config = StackingConfig.Default.WithItem(a).WithItem(b);
        Assert.That(config.Items.Select(item => item.ItemId), Is.EqualTo(new[] { "a", "b" }));

        var replaced = config.WithItem(a.WithEnabled(false));
        Assert.That(replaced.Items.Select(item => item.ItemId), Is.EqualTo(new[] { "a", "b" }), "A replaced rule keeps its place.");
        Assert.That(replaced.Find("a")!.Enabled, Is.False);
        Assert.That(config.WithoutItem("a").Items.Single().ItemId, Is.EqualTo("b"));
        Assert.That(config.WithAllEnabled(false).Items.Any(item => item.Enabled), Is.False);
        var tinted = config.WithMarker(new StackingMarker(0xFF5C5C)).WithAdvanced(true);
        Assert.That(tinted.WithoutItems().Items, Is.Empty);
        Assert.That(tinted.WithoutItems().Marker.Rgb, Is.EqualTo(0xFF5C5C), "Deleting every rule keeps the marker.");
        Assert.That(tinted.WithoutItems().Advanced, Is.True);
        Assert.That(StackingConfig.Default.Items, Is.Empty);
        Assert.That(config.Find("missing"), Is.Null);
    }

    [Test]
    public void ItemRule_IgnoredFieldsKeepExistingModesAndAppendNewRows()
    {
        var rule = new StackingItemRule("a", false, new[] { new StackingRule("x", StackingMode.Match) });

        var extended = rule.WithIgnoredFields(new[] { "x", "y" });

        Assert.That(extended.Rules, Is.EqualTo(new[]
        {
            new StackingRule("x", StackingMode.Match), new StackingRule("y", StackingMode.Ignore)
        }));
        Assert.That(extended.Enabled, Is.False, "Adding rows keeps the switch.");
        Assert.That(extended.IsActive, Is.False);
        Assert.That(extended.WithEnabled(true).IsActive, Is.True);
    }

    [Test]
    public void RoundTrip_KeepsGlobalRowsTheirSwitchesAndOrder()
    {
        var config = new StackingConfig(new[]
        {
            new StackingItemRule("a", true, new[] { new StackingRule("price_composition", StackingMode.Match) })
        }, new StackingMarker(0xB37FEB), advanced: false, new[]
        {
            new StackingGlobalRule("tag:DURABILITY", StackingMode.Ignore, true),
            new StackingGlobalRule("price_composition", StackingMode.Ignore, false),
            new StackingGlobalRule("feature:光泽", StackingMode.Match, true)
        });
        var text = StackingConfigCodec.Encode(config);
        Assert.That(text, Does.StartWith("v5\nmarker\t#B37FEB\nadvanced\t0\nglobal\t"));
        var decoded = StackingConfigCodec.Decode(text);
        Assert.That(decoded.Status, Is.EqualTo(StackingConfigStatus.Ok));
        Assert.That(decoded.Config, Is.EqualTo(config));
    }

    [Test]
    public void EarlierVersion_IsReadAsDefault()
    {
        var result = StackingConfigCodec.Decode("v4\nmarker\t#B37FEB\nadvanced\t1\nitem\tYQ==\t1\nrule\tYg==\tignore");
        Assert.That(result.Status, Is.EqualTo(StackingConfigStatus.Missing));
        Assert.That(result.Config, Is.EqualTo(StackingConfig.Default));
    }

    [Test]
    public void EffectiveRule_ItemRowsOverrideGlobalRowsFieldByField()
    {
        var config = StackingConfig.Default.WithGlobals(new[]
        {
            new StackingGlobalRule("x", StackingMode.Ignore, true),
            new StackingGlobalRule("y", StackingMode.Ignore, true),
            new StackingGlobalRule("z", StackingMode.Ignore, false)
        }).WithItem(new StackingItemRule("a", true, new[] { new StackingRule("x", StackingMode.Match) }));

        Assert.That(config.EffectiveRule("a")!.Rules, Is.EqualTo(new[]
        {
            new StackingRule("x", StackingMode.Match), new StackingRule("y", StackingMode.Ignore)
        }), "The item row wins, a switched-off global row is left out.");
        Assert.That(config.EffectiveRule("b")!.Rules, Is.EqualTo(new[]
        {
            new StackingRule("x", StackingMode.Ignore), new StackingRule("y", StackingMode.Ignore)
        }), "An item without a rule follows the global rows.");
        var off = config.WithItem(config.Find("a")!.WithEnabled(false));
        Assert.That(off.EffectiveRule("a")!.Rules, Is.EqualTo(new[]
        {
            new StackingRule("x", StackingMode.Ignore), new StackingRule("y", StackingMode.Ignore)
        }), "A switched-off item rule leaves only the global rows.");
        Assert.That(StackingConfig.Default.EffectiveRule("a"), Is.Null);
        Assert.That(config.WithAllGlobalsEnabled(false).HasActiveRule, Is.False);
        Assert.That(config.HasActiveRule, Is.True);
    }

    [Test]
    public void GlobalEdits_ReturnNewConfigurations()
    {
        var config = StackingConfig.Default.WithGlobal(new StackingGlobalRule("x", StackingMode.Ignore, true))
            .WithGlobal(new StackingGlobalRule("y", StackingMode.Match, true));
        var replaced = config.WithGlobal(config.FindGlobal("x")!.WithEnabled(false));
        Assert.That(replaced.Globals.Select(row => row.FieldId), Is.EqualTo(new[] { "x", "y" }), "A replaced row keeps its place.");
        Assert.That(replaced.FindGlobal("x")!.Enabled, Is.False);
        Assert.That(config.WithoutGlobal("x").Globals.Single().FieldId, Is.EqualTo("y"));
        Assert.That(config.WithItem(new StackingItemRule("a", true, Array.Empty<StackingRule>())).WithoutItems().Globals,
            Is.EqualTo(config.Globals), "Deleting every item rule keeps the global rows.");
        Refused(() => _ = config.WithGlobals(new[]
        {
            new StackingGlobalRule("x", StackingMode.Ignore, true), new StackingGlobalRule("x", StackingMode.Match, false)
        }));
        Refused(() => _ = config.WithGlobals(Enumerable.Range(0, StackingConfig.MaxGlobals + 1)
            .Select(i => new StackingGlobalRule("f" + i, StackingMode.Ignore, true))));
    }

    private static void Refused(Action code) => Assert.Throws<ArgumentException>(code);
}
