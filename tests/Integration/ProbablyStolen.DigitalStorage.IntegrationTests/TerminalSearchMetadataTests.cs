using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

public sealed class TerminalSearchMetadataTests
{
    private static PinyinSearchAddon Pinyin() => new(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Resources", "pinyin-dictionary.txt")), new SearchConfiguration());

    private static TerminalSearchService Service(PinyinSearchAddon addon, SearchConfiguration? configuration = null)
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, config => config.Tags));
        registry.RegisterField(new SearchFieldProvider("types", document => document.ItemTypes, config => config.ItemTypes));
        registry.RegisterPrefix('#', "tags");
        registry.RegisterPrefix('@', "types");
        registry.RegisterMatcher("pinyin", addon.Matches);
        return new TerminalSearchService(registry, configuration ?? new SearchConfiguration());
    }

    [TestCase("#日用")]
    [TestCase("#riyong")]
    [TestCase("#RIYONG")]
    [TestCase("#ryp")]
    [TestCase("#ri用")]
    [TestCase("#ri4yong4")]
    [TestCase("@日用品")]
    [TestCase("@riyong")]
    [TestCase("#household_good")]
    [TestCase("@household_good")]
    public void LocalizedTypeTag_MatchesChinesePinyinAndOriginalIdentifier(string query)
    {
        using var addon = Pinyin();
        var service = Service(addon);
        var raw = new SearchDocument("Household sample", tagNames: new[] { "HOUSEHOLD_GOOD", "TYPE-STRING_HOUSEHOLD_GOOD" },
            itemTypes: new[] { "HOUSEHOLD_GOOD" });
        Assert.That(service.Matches(raw, "#riyong"), Is.False, "Raw English identifiers contain no Chinese pinyin.");
        var document = TerminalSearchMetadata.WithTypeNames(raw, new[] { "HOUSEHOLD_GOOD" },
            type => type == "HOUSEHOLD_GOOD" ? "日用品" : type);
        Assert.That(service.Matches(document, query), Is.True);
        Assert.That(service.Matches(document, "riyong"), Is.False, "The approved modes still require prefixes.");
        Assert.That(raw.TagNames, Does.Not.Contain("日用品"));
        Assert.That(raw.ItemTypes, Does.Not.Contain("日用品"));
    }

    [Test]
    public void TypeWithoutEnabledSameIdTag_DoesNotBecomeATagSearchHit()
    {
        using var addon = Pinyin();
        var service = Service(addon);
        // An unrelated tag's readable name may coincide with a type ID; only its actual key counts.
        var raw = new SearchDocument("Sample", tagNames: new[] { "OTHER_TAG", "HOUSEHOLD_GOOD" },
            itemTypes: new[] { "HOUSEHOLD_GOOD" });
        var document = TerminalSearchMetadata.WithTypeNames(raw, new[] { "OTHER_TAG" }, _ => "日用品");
        Assert.That(service.Matches(document, "#riyong"), Is.False);
        Assert.That(service.Matches(document, "@riyong"), Is.True);
    }

    [Test]
    public void EnabledTagWithoutItemType_IsNotGivenATypeTranslation()
    {
        var calls = 0;
        var raw = new SearchDocument("Sample", tagNames: new[] { "HOUSEHOLD_GOOD" });
        var document = TerminalSearchMetadata.WithTypeNames(raw, new[] { "HOUSEHOLD_GOOD" }, _ =>
        {
            calls++;
            return "日用品";
        });
        Assert.That(document.TagNames, Is.EqualTo(raw.TagNames));
        Assert.That(document.ItemTypes, Is.Empty);
        Assert.That(calls, Is.Zero);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("UNKNOWN_TYPE")]
    public void MissingTranslation_PreservesRawTermsWithoutInventingChinese(string? translation)
    {
        var raw = new SearchDocument("Sample", tagNames: new[] { "UNKNOWN_TYPE" }, itemTypes: new[] { "UNKNOWN_TYPE" });
        var document = TerminalSearchMetadata.WithTypeNames(raw, new[] { "UNKNOWN_TYPE" }, _ => translation);
        Assert.That(document.HasSameContent(raw), Is.True);
    }

    [Test]
    public void LocalizationIsCapturedOnceAndModesRemainIndependent()
    {
        using var addon = Pinyin();
        var service = Service(addon);
        var calls = 0;
        var raw = new SearchDocument("Sample", "sample.id", new[] { "HOUSEHOLD_GOOD" },
            new[] { "HOUSEHOLD_GOOD", "HOUSEHOLD_GOOD" }, new[] { "description" },
            new[] { new KeyValuePair<string, IEnumerable<string>>("addon", new[] { "detached" }) });
        var document = TerminalSearchMetadata.WithTypeNames(raw, new[] { "HOUSEHOLD_GOOD" }, _ =>
        {
            calls++;
            return "日用品";
        });
        Assert.That(document.ItemTypes.Count(term => term == "日用品"), Is.EqualTo(1));
        Assert.That(document.AddonFields["addon"], Is.EqualTo(new[] { "detached" }));
        Assert.That(document.Descriptions, Is.EqualTo(raw.Descriptions));
        Assert.That(document.Identifier, Is.EqualTo(raw.Identifier));
        service.UpdateConfiguration(new SearchConfiguration(tags: SearchMode.Disabled));
        Assert.That(service.Matches(document, "#riyong"), Is.False);
        Assert.That(service.Matches(document, "@riyong"), Is.True);
        service.UpdateConfiguration(new SearchConfiguration(itemTypes: SearchMode.Disabled));
        Assert.That(service.Matches(document, "#riyong"), Is.True);
        Assert.That(service.Matches(document, "@riyong"), Is.False);
        service.UpdateConfiguration(new SearchConfiguration(tags: SearchMode.Enabled));
        Assert.That(service.Matches(document, "riyong"), Is.True);
        Assert.That(calls, Is.EqualTo(1), "Typing and configuration changes must not resolve native labels.");
    }
}
