using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Api.Core.Tests;

[TestFixture]
public sealed class ItemKeyTests
{
    [Test]
    public void Of_SortsComponentsByOrdinalName()
    {
        var key = new ItemKey("item.tool", new[]
        {
            new KeyValuePair<string, string>("z_state", "9"),
            new KeyValuePair<string, string>("a_state", "1")
        });

        Assert.That(key.Components.Select(component => component.Key), Is.EqualTo(new[] { "a_state", "z_state" }));
        Assert.That(key.Identifier, Is.EqualTo("item.tool"));
        Assert.That(key.GetPrimaryKey(), Is.EqualTo("item.tool"));
        Assert.That(ItemKey.Of("item.tool", "state", "1").Components.Single().Value, Is.EqualTo("1"));
    }

    [Test]
    public void Components_NullValue_IsNormalizedToEmptyString()
    {
        var key = new ItemKey("item.tool", new[] { new KeyValuePair<string, string>("state", null!) });

        Assert.That(key.Components.Single().Value, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Equals_ComparesIdentifierAndCompleteComponentState()
    {
        var left = new ItemKey("item.tool", new[] { new KeyValuePair<string, string>("state", "a") });
        var sameState = new ItemKey("item.tool", new[] { new KeyValuePair<string, string>("state", "a") });
        var otherState = new ItemKey("item.tool", new[] { new KeyValuePair<string, string>("state", "b") });
        var otherIdentifier = new ItemKey("item.other", new[] { new KeyValuePair<string, string>("state", "a") });
        var noState = new ItemKey("item.tool");

        Assert.That(left, Is.EqualTo(sameState));
        Assert.That(left.GetHashCode(), Is.EqualTo(sameState.GetHashCode()));
        Assert.That(left, Is.Not.EqualTo(otherState));
        Assert.That(left, Is.Not.EqualTo(otherIdentifier));
        Assert.That(left, Is.Not.EqualTo(noState));
        Assert.That(left.Equals((ItemKey?)null), Is.False);
    }

    [Test]
    public void CompareTo_UsesOrdinalFullComponentPairsBeforeLength()
    {
        var none = new ItemKey("item.tool");
        var alpha = ItemKey.Of("item.tool", "a", "z");
        var alphaExtended = new ItemKey("item.tool", new[]
        {
            new KeyValuePair<string, string>("a", "z"),
            new KeyValuePair<string, string>("z", "0")
        });
        var beta = ItemKey.Of("item.tool", "b", "a");
        var upper = new ItemKey("Item.tool");
        var unordered = new[] { beta, alphaExtended, none, alpha, upper };

        Assert.That(unordered.OrderBy(key => key), Is.EqualTo(new[] { upper, none, alpha, alphaExtended, beta }));
        Assert.That(alpha.CompareTo(ItemKey.Of("item.tool", "a", "z")), Is.Zero);
        Assert.That(alpha.CompareTo(null), Is.GreaterThan(0));
        Assert.That(Math.Sign(alpha.CompareTo(beta)), Is.EqualTo(-Math.Sign(beta.CompareTo(alpha))));
    }

    [Test]
    public void CompareTo_NullAndTransitiveAcrossOrdinalValuesInDifferentCultures()
    {
        var keys = new ItemKey?[]
        {
            null,
            new("I"),
            new("a"),
            new("a\0"),
            new("a", new[] { new KeyValuePair<string, string>("x", "") }),
            ItemKey.Of("a", "x", "\0"),
            ItemKey.Of("a", "x", "z"),
            ItemKey.Of("a", "\ud800", "z"),
            new("\u0130")
        };
        var comparer = Comparer<ItemKey?>.Default;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var sorted = keys.OrderBy(key => key, comparer).ToArray();
                for (var i = 0; i < sorted.Length; i++)
                {
                    for (var j = i; j < sorted.Length; j++)
                    {
                        Assert.That(comparer.Compare(sorted[i], sorted[j]), Is.LessThanOrEqualTo(0));
                        Assert.That(Math.Sign(comparer.Compare(sorted[i], sorted[j])),
                            Is.EqualTo(-Math.Sign(comparer.Compare(sorted[j], sorted[i]))));
                    }
                }

                Assert.That(sorted[0], Is.Null);
                Assert.That(comparer.Compare(null, null), Is.Zero);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void CompareTo_DeterministicLegacyKeys_AgreesWithEqualityAndIsTransitive()
    {
        var random = new Random(9317);
        var names = new[] { "a", "aa", "b", "k0", "k1", "\ud800" };
        var keys = Enumerable.Range(0, 60).Select(_ => new ItemKey(
            new[] { "A", "a", "a\0", "\ud800" }[random.Next(4)],
            names.OrderBy(_ => random.Next()).Take(random.Next(4)).Select(name =>
                new KeyValuePair<string, string>(name,
                    new[] { "", "\0", "a", "aa", "\udfff" }[random.Next(5)]))))
            .Concat(new[] { ItemKey.Of("a", "a", "x"), ItemKey.Of("a", "b", "x") }).ToArray();

        foreach (var first in keys)
            foreach (var second in keys)
            {
                Assert.That(first.CompareTo(second) == 0, Is.EqualTo(first.Equals(second)));
                Assert.That(Math.Sign(first.CompareTo(second)), Is.EqualTo(-Math.Sign(second.CompareTo(first))));
                foreach (var third in keys)
                {
                    if (first.CompareTo(second) <= 0 && second.CompareTo(third) <= 0)
                    {
                        Assert.That(first.CompareTo(third), Is.LessThanOrEqualTo(0));
                    }
                }
            }
    }

    [Test]
    public void Operators_CompareValuesAndHandleNull()
    {
        var left = ItemKey.Of("item.tool", "state", "a");
        var sameState = ItemKey.Of("item.tool", "state", "a");
        var otherState = ItemKey.Of("item.tool", "state", "b");
        ItemKey? missing = null;

        Assert.That(left == sameState, Is.True);
        Assert.That(left != otherState, Is.True);
        Assert.That(missing == null, Is.True);
        Assert.That(missing != left, Is.True);
        Assert.That(missing == left, Is.False);
        Assert.That(left == missing, Is.False);
    }

    [Test]
    public void Constructor_RejectsBlankIdentifierAndDuplicateComponentNames()
    {
        Assert.Throws<ArgumentException>(new Action(() => new ItemKey("  ")));
        Assert.Throws<ArgumentException>(new Action(() => new ItemKey("item.tool", new[]
        {
            new KeyValuePair<string, string>("state", "1"),
            new KeyValuePair<string, string>("state", "2")
        })));
        Assert.Throws<ArgumentException>(new Action(() => new ItemKey("item.tool", new[]
        {
            new KeyValuePair<string, string>(string.Empty, "1")
        })));
    }
}
