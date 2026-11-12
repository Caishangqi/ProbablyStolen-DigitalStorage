using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using System;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

public sealed class TerminalSearchServiceTests
{
    private static TerminalSearchService CreateService(SearchConfiguration? configuration = null)
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, configuration => configuration.Tags));
        registry.RegisterField(new SearchFieldProvider("types", document => document.ItemTypes, configuration => configuration.ItemTypes));
        registry.RegisterField(new SearchFieldProvider("descriptions", document => document.Descriptions, configuration => configuration.Descriptions));
        registry.RegisterField(new SearchFieldProvider("identifier", document => new[] { document.Identifier }, configuration => configuration.Identifiers));
        registry.RegisterPrefix('#', "tags");
        registry.RegisterPrefix('@', "types");
        registry.RegisterPrefix('$', "descriptions");
        registry.RegisterPrefix('&', "identifier");
        registry.RegisterMatcher("pinyin", (source, query) =>
        {
            var map = new[] { ("材料", "cailiao"), ("测试", "ceshi"), ("重庆", "chongqing") };
            return map.Any(pair => source.Contains(pair.Item1, StringComparison.Ordinal) &&
                pair.Item2.StartsWith(query, StringComparison.Ordinal));
        });
        return new TerminalSearchService(registry, configuration ?? new SearchConfiguration());
    }

    [Test]
    public void Search_NameAndPrefixModes_UseApprovedDefaults()
    {
        var service = CreateService();
        var document = new SearchDocument("Materials", "digital.material", new[] { "Materials" },
            new[] { "Crafting" }, new[] { "A useful material" });

        Assert.That(service.Matches(document, "material"), Is.True);
        Assert.That(service.Matches(document, "#material"), Is.True);
        Assert.That(service.Matches(document, "@material"), Is.False);
        Assert.That(service.Matches(document, "&digital"), Is.False);
        Assert.That(service.Matches(document, "unknown:value"), Is.False);
        Assert.That(service.Matches(document, "#"), Is.False);
    }

    [Test]
    public void Search_AndOrExclusionAndQuotes_AreDeterministic()
    {
        var service = CreateService();
        var document = new SearchDocument("Materials", "digital.material", new[] { "Materials" },
            descriptions: new[] { "A useful material" });

        Assert.That(service.Matches(document, "material useful"), Is.True);
        Assert.That(service.Matches(document, "missing | \"useful material\""), Is.True);
        Assert.That(service.Matches(document, "material -missing"), Is.True);
        Assert.That(service.Matches(document, "material -useful"), Is.False);
        Assert.That(service.Matches(document, "\\|"), Is.False);
        Assert.That(service.Matches(document, "\\"), Is.False);
        Assert.That(service.Matches(document, "-"), Is.False);
    }

    [Test]
    public void Search_ColorSpans_KeepRawPositionsAndJeiColors()
    {
        var service = CreateService();
        var spans = service.GetColorSpans("#材料 | -foo", matched: true);

        Assert.That(spans.Any(span => span.Start == 0 && span.Length == 1 && span.Kind == SearchColorKind.Prefix), Is.True);
        Assert.That(spans.Any(span => span.Start == 1 && span.Length == 2 && span.Kind == SearchColorKind.PrefixedText), Is.True);
        Assert.That(spans.Any(span => span.Kind == SearchColorKind.Operator), Is.True);
        Assert.That(TerminalSearchService.PrefixArgb, Is.EqualTo(0xFF55FFFFu));
        Assert.That(TerminalSearchService.PrefixedArgb, Is.EqualTo(0xFF55FF55u));
        var quoted = service.GetColorSpans("#\"material\"", matched: true);
        Assert.That(quoted.Any(span => span.Kind == SearchColorKind.Prefix), Is.True);
        Assert.That(quoted.Any(span => span.Kind == SearchColorKind.PrefixedText), Is.False);
        var mixed = service.GetColorSpans("#foo\"bar\"baz", matched: true)
            .Where(span => span.Kind == SearchColorKind.PrefixedText).ToArray();
        Assert.That(mixed.Select(span => (span.Start, span.Length)), Is.EqualTo(new[] { (1, 3), (9, 3) }));
    }

    [Test]
    public void Search_PinyinAddon_RebuildsOnlyForChangedEnabledOptions()
    {
        var loads = 0;
        using var addon = new PinyinSearchAddon(new SearchConfiguration(), () => loads++);
        addon.LoadDictionary("测: ce4\n试: shi4\n");
        Assert.That(loads, Is.EqualTo(1));
        addon.Configure(new SearchConfiguration(enabled: false, zh2z: true));
        addon.Configure(new SearchConfiguration(enabled: false, sh2s: true));
        Assert.That(loads, Is.EqualTo(1));
        addon.Configure(new SearchConfiguration(sh2s: true));
        Assert.That(loads, Is.EqualTo(2));
        Assert.That(addon.Matches("试", "si"), Is.True);
        addon.Configure(new SearchConfiguration(sh2s: true, tags: SearchMode.Disabled, syntaxHighlighting: false));
        addon.Configure(new SearchConfiguration(enabled: false, sh2s: true));
        addon.Configure(new SearchConfiguration(sh2s: true));
        Assert.That(loads, Is.EqualTo(2));
    }

    [Test]
    public void Search_PinyinAddon_MatchesToneAndMixedQueries()
    {
        const string dictionary = "测: ce4\n试: shi4\n材: cai2\n料: liao4\n重: zhong4, chong2\n庆: qing4\n";
        using var addon = new PinyinSearchAddon(dictionary, new SearchConfiguration());
        Assert.That(addon.Matches("测试", "ceshi"), Is.True);
        Assert.That(addon.Matches("测试", "ce4shi4"), Is.True);
        Assert.That(addon.Matches("测试", "cs"), Is.True);
        Assert.That(addon.Matches("材料", "cai料"), Is.True);
        Assert.That(addon.Matches("材料", "cailiao"), Is.True);
        Assert.That(addon.Matches("重庆", "chongqing"), Is.True);
        Assert.That(addon.Matches("测试", "ce2"), Is.False);
    }

    [Test]
    public void Search_BundledDictionary_ParsesSimplifiedTraditionalAndPolyphonicEntries()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "pinyin-dictionary.txt");
        var dictionary = System.IO.File.ReadAllText(path);
        using var addon = new PinyinSearchAddon(dictionary, new SearchConfiguration());
        Assert.That(addon.Matches("材料", "cailiao"), Is.True);
        Assert.That(addon.Matches("慶試", "qingshi"), Is.True);
        Assert.That(addon.Matches("重慶", "chongqing"), Is.True);
    }

    [Test]
    public void Search_DeferredDictionary_ActivatesAnAlreadyRegisteredMatcher()
    {
        var configuration = new SearchConfiguration();
        using var addon = new PinyinSearchAddon(configuration);
        var registry = new SearchMatcherRegistry();
        registry.RegisterMatcher("pinyin", addon.Matches);
        var service = new TerminalSearchService(registry, configuration);
        var document = new SearchDocument("水质检测试剂");
        Assert.That(service.Matches(document, "水"), Is.True);
        Assert.That(service.Matches(document, "shui"), Is.False);
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "pinyin-dictionary.txt");
        addon.LoadDictionary(System.IO.File.ReadAllText(path));
        service.UpdateConfiguration(configuration);
        Assert.That(service.Matches(document, "shui"), Is.True);
        Assert.That(service.Matches(document, "SHUI"), Is.True);
        addon.Dispose();
        Assert.That(service.Matches(document, "shui"), Is.False);
        Assert.That(service.Matches(document, "水"), Is.True);
    }

    [Test]
    public void Search_DeferredDictionary_RetainsOptionsAppliedBeforeResourcesAreReady()
    {
        using var addon = new PinyinSearchAddon(new SearchConfiguration());
        var disabled = new SearchConfiguration(keyboard: PinyinKeyboard.Xiaohe, enabled: false);
        addon.Configure(disabled);
        addon.LoadDictionary("水: shui3\n");
        Assert.That(addon.Matches("水质检测试剂", "uv"), Is.False);
        addon.Configure(new SearchConfiguration(keyboard: PinyinKeyboard.Xiaohe));
        Assert.That(addon.Matches("水质检测试剂", "uv"), Is.True);
        Assert.That(addon.Matches("水质检测试剂", "shui"), Is.False);
    }

    [Test]
    public void Search_DeferredDictionary_InvalidDataLeavesLiteralSearchAvailable()
    {
        using var addon = new PinyinSearchAddon(new SearchConfiguration());
        var registry = new SearchMatcherRegistry();
        registry.RegisterMatcher("pinyin", addon.Matches);
        var service = new TerminalSearchService(registry, new SearchConfiguration());
        Action load = () => addon.LoadDictionary("invalid dictionary");
        Assert.Throws<System.IO.InvalidDataException>(load);
        var document = new SearchDocument("水质检测试剂");
        Assert.That(service.Matches(document, "shui"), Is.False);
        Assert.That(service.Matches(document, "水质"), Is.True);
    }

    [Test]
    public void Search_BundledDictionary_WaterTestReagentMatchesShui()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "pinyin-dictionary.txt");
        using var addon = new PinyinSearchAddon(System.IO.File.ReadAllText(path), new SearchConfiguration());
        var registry = new SearchMatcherRegistry();
        registry.RegisterMatcher("pinyin", addon.Matches);
        var service = new TerminalSearchService(registry, new SearchConfiguration());
        var document = new SearchDocument("水质检测试剂");
        Assert.That(service.Matches(document, "shui"), Is.True);
        Assert.That(service.Matches(document, "SHUI"), Is.True);
        Assert.That(service.Matches(document, "shui3zhi4"), Is.True);
    }

    [TestCase(PinyinKeyboard.Daqian, "5j/")]
    [TestCase(PinyinKeyboard.Xiaohe, "vs")]
    [TestCase(PinyinKeyboard.Ziranma, "vs")]
    [TestCase(PinyinKeyboard.Sougou, "vs")]
    [TestCase(PinyinKeyboard.Guobiao, "vs")]
    [TestCase(PinyinKeyboard.Microsoft, "vs")]
    [TestCase(PinyinKeyboard.PinyinPp, "vy")]
    [TestCase(PinyinKeyboard.Ziguang, "uh")]
    public void Search_AllKeyboardLayouts_MatchTheSameReading(PinyinKeyboard keyboard, string query)
    {
        const string dictionary = "重: zhong1\n";
        using var addon = new PinyinSearchAddon(dictionary, new SearchConfiguration(keyboard: keyboard));
        Assert.That(addon.Matches("重", query), Is.True);
    }

    [TestCase("中", "zhong1", "zong1", "zh2z")]
    [TestCase("上", "shang4", "sang4", "sh2s")]
    [TestCase("吃", "chi1", "ci1", "ch2c")]
    [TestCase("昂", "ang2", "an2", "ang2an")]
    [TestCase("英", "ying1", "yin1", "ing2in")]
    [TestCase("冷", "leng3", "len3", "eng2en")]
    [TestCase("女", "nv3", "nu3", "u2v")]
    public void Search_FuzzySwitch_MatchesOnlyWhenEnabled(string character, string reading, string query, string option)
    {
        var dictionary = character + ": " + reading + "\n";
        using var defaultAddon = new PinyinSearchAddon(dictionary, new SearchConfiguration());
        Assert.That(defaultAddon.Matches(character, query), Is.False);
        var enabled = option switch
        {
            "zh2z" => new SearchConfiguration(zh2z: true),
            "sh2s" => new SearchConfiguration(sh2s: true),
            "ch2c" => new SearchConfiguration(ch2c: true),
            "ang2an" => new SearchConfiguration(ang2an: true),
            "ing2in" => new SearchConfiguration(ing2in: true),
            "eng2en" => new SearchConfiguration(eng2en: true),
            _ => new SearchConfiguration(u2v: true)
        };
        using var fuzzyAddon = new PinyinSearchAddon(dictionary, enabled);
        Assert.That(fuzzyAddon.Matches(character, query), Is.True);
    }

    [Test]
    public void Search_InvalidDictionaryAndLongOrUnicodeQueries_FailSafely()
    {
        Action invalidDictionary = () => new PinyinSearchAddon("not a dictionary", new SearchConfiguration());
        Assert.Throws<System.IO.InvalidDataException>(invalidDictionary);
        using var addon = new PinyinSearchAddon("测: ce4\n", new SearchConfiguration());
        Assert.That(addon.Matches("测", new string('c', 80)), Is.False);
        Assert.That(addon.Matches("测🙂", "🙂"), Is.True);
        Assert.That(addon.Matches("测", "🙂"), Is.False);
    }

    [Test]
    public void Search_ConfigurationUpdate_ChangesEnabledFields()
    {
        var service = CreateService();
        var document = new SearchDocument("Materials", "digital.material", new[] { "Materials" });
        Assert.That(service.Matches(document, "materials"), Is.True);
        service.UpdateConfiguration(new SearchConfiguration(tags: SearchMode.Disabled));
        Assert.That(service.Matches(document, "#materials"), Is.False);
        Assert.That(service.Matches(document, "materials"), Is.True);
    }

    [Test]
    public void Search_LongSourceAndManyQuotes_CompleteWithoutRecursiveStack()
    {
        using var addon = new PinyinSearchAddon("测: ce4\n", new SearchConfiguration());
        Assert.That(addon.Matches(new string('测', 5000), new string('c', 4999) + "x"), Is.False);
        var query = "#" + string.Concat(Enumerable.Repeat("a\"b\"", 5000));
        var spans = CreateService().GetColorSpans(query, true);
        Assert.That(spans.Count, Is.GreaterThan(1000));
    }

    [Test]
    public void Search_CaseAndFuzzyUToV_AreIndependentOfCase()
    {
        using var addon = new PinyinSearchAddon("路: lu4\n女: nv3\n测: ce4\n试: shi4\n", new SearchConfiguration(u2v: true));
        Assert.That(addon.Matches("路", "LV4"), Is.True);
        Assert.That(addon.Matches("女", "NU3"), Is.True);
        Assert.That(addon.Matches("测试", "CESHI"), Is.True);
        Assert.That(addon.Matches("测试", "CE试"), Is.True);
    }

    [Test]
    public void Search_IndependentAddonStillRunsWithPinyinDisabled()
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterMatcher("independent", (source, query) => source == "name" && query == "alias");
        registry.RegisterField(new SearchFieldProvider("addon", document => document.AddonFields["addon"], _ => SearchMode.RequirePrefix));
        registry.RegisterPrefix('%', "addon");
        var fields = new[] { new System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.IEnumerable<string>>("addon", new[] { "value" }) };
        var document = new SearchDocument("name", addonFields: fields);
        var service = new TerminalSearchService(registry, new SearchConfiguration(enabled: false));
        Assert.That(service.Matches(document, "alias"), Is.True);
        Assert.That(service.Matches(document, "%value"), Is.True);
        Action duplicateFields = () => new SearchDocument("name", addonFields: fields.Concat(fields));
        Assert.Throws<ArgumentException>(duplicateFields);
    }

    [TestCase(0, 2, 3, 0)]
    [TestCase(2, 2, 3, -1)]
    [TestCase(4, 2, 3, -1)]
    [TestCase(5, 2, 3, 2)]
    [TestCase(7, 5, 2, 5)]
    public void Search_CompositionMapping_ExcludesInsertedGlyphs(int rendered, int insertion, int length, int expected)
    {
        Assert.That(TerminalSearchService.CommittedIndex(rendered, insertion, length), Is.EqualTo(expected));
    }

    [Test]
    public void Search_Registry_RejectsDuplicateRegistrations()
    {
        var registry = new SearchMatcherRegistry();
        var unknownField = new SearchFieldProvider("unknown", document => new[] { document.Name }, _ => SearchMode.Enabled);
        Action unknownPrefix = () => registry.RegisterPrefix('#', "unknown");
        Assert.Throws<InvalidOperationException>(unknownPrefix);
        registry.RegisterField(unknownField);
        Action duplicateField = () => registry.RegisterField(unknownField);
        Assert.Throws<InvalidOperationException>(duplicateField);
        registry.RegisterPrefix('#', "unknown");
        Action duplicatePrefix = () => registry.RegisterPrefix('#', "unknown");
        Assert.Throws<InvalidOperationException>(duplicatePrefix);
        registry.RegisterMatcher("plain", (_, _) => true);
        Action duplicateMatcher = () => registry.RegisterMatcher("plain", (_, _) => true);
        Assert.Throws<InvalidOperationException>(duplicateMatcher);
    }

    [Test]
    public void MatchesText_UsesSubstringAndPinyinWithoutQuerySyntax()
    {
        var service = CreateService();
        Assert.That(service.MatchesText("测试材料", "材料"), Is.True);
        Assert.That(service.MatchesText("Water Bottle", " bottle "), Is.True, "Case-insensitive and trimmed.");
        Assert.That(service.MatchesText("测试材料", "cailiao"), Is.True, "Registered matchers such as pinyin apply.");
        Assert.That(service.MatchesText("测试", "-测试"), Is.False, "A leading minus is text, not a negation.");
        Assert.That(service.MatchesText("#tag", "#tag"), Is.True, "A prefix character is text, not a field filter.");
        Assert.That(service.MatchesText("测试", "   "), Is.False);
    }
}
