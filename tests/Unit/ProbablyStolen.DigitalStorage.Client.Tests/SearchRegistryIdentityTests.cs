using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class SearchRegistryIdentityTests
{
    [Test]
    public void ArbitraryIdsStayDistinctAndMatchersRunInRegistrationOrder()
    {
        var calls = new List<string>();
        var registry = new SearchMatcherRegistry();
        registry.RegisterMatcher("z.a", (_, _) => { calls.Add("z.a"); return false; });
        registry.RegisterMatcher("z_a", (_, _) => { calls.Add("z_a"); return true; });
        registry.RegisterField(new SearchFieldProvider("A:b", _ => new[] { "value" }, _ => SearchMode.Enabled));
        registry.RegisterField(new SearchFieldProvider("A.b", _ => Array.Empty<string>(), _ => SearchMode.Enabled));
        registry.RegisterPrefix('@', "A:b");
        var search = new TerminalSearchService(registry, new SearchConfiguration());
        Assert.That(search.MatchesText("source", "query"), Is.True);
        Assert.That(calls, Is.EqualTo(new[] { "z.a", "z_a" }));
        Assert.That(search.Matches(new SearchDocument("name"), "@value"), Is.True);
        Assert.Throws<InvalidOperationException>((Action)(() => registry.RegisterMatcher("late", (_, _) => true)));
    }
}
