using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class SearchHintTests
{
    [Test]
    public void Lines_ListTheSwitchedOnPrefixesThenOperatorsPinyinAndClear()
    {
        var lines = TerminalSearchHint.Lines(Search(new SearchConfiguration()), syntax: true);

        Assert.That(lines.Select(line => (line.Key, line.Prefix)), Is.EqualTo(new (string, char?)[]
        {
            ("terminal_search_tip_tags", '#'), ("terminal_search_tip_types", '@'),
            ("terminal_search_tip_descriptions", '$'),
            ("terminal_search_tip_negate", '-'), ("terminal_search_tip_or", '|'), ("terminal_search_tip_quote", '"'),
            (TerminalSearchHint.PinyinKey, null), (TerminalSearchHint.ClearKey, null)
        }), "The identifier prefix is off by default, so it is not offered.");
    }

    [Test]
    public void Lines_FollowTheSettings()
    {
        var configuration = new SearchConfiguration(enabled: false, tags: SearchMode.Disabled,
            identifiers: SearchMode.RequirePrefix);
        var keys = TerminalSearchHint.Lines(Search(configuration), syntax: true).Select(line => line.Key).ToArray();

        Assert.That(keys, Does.Contain("terminal_search_tip_identifier"));
        Assert.That(keys, Does.Not.Contain("terminal_search_tip_tags"));
        Assert.That(keys, Does.Not.Contain(TerminalSearchHint.PinyinKey));
    }

    [Test]
    public void Lines_WithoutSyntaxOfferOnlyPinyinAndClear()
    {
        Assert.That(TerminalSearchHint.Lines(Search(new SearchConfiguration()), syntax: false).Select(line => line.Key),
            Is.EqualTo(new[] { TerminalSearchHint.PinyinKey, TerminalSearchHint.ClearKey }));
    }

    [Test]
    public void Lines_SkipPrefixesOfUnknownFields()
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("addon.color", _ => new[] { "red" }, _ => SearchMode.Enabled));
        registry.RegisterPrefix('%', "addon.color");
        var lines = TerminalSearchHint.Lines(new TerminalSearchService(registry, new SearchConfiguration()), syntax: true);

        Assert.That(lines.Any(line => line.Prefix == '%'), Is.False);
    }

    private static TerminalSearchService Search(SearchConfiguration configuration)
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, config => config.Tags));
        registry.RegisterField(new SearchFieldProvider("types", document => document.ItemTypes, config => config.ItemTypes));
        registry.RegisterField(new SearchFieldProvider("descriptions", document => document.Descriptions, config => config.Descriptions));
        registry.RegisterField(new SearchFieldProvider("identifier", document => new[] { document.Identifier }, config => config.Identifiers));
        registry.RegisterPrefix('#', "tags");
        registry.RegisterPrefix('@', "types");
        registry.RegisterPrefix('$', "descriptions");
        registry.RegisterPrefix('&', "identifier");
        return new TerminalSearchService(registry, configuration);
    }
}
