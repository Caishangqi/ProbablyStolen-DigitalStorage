using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class PinyinDropdownDisplayTests
{
    [TestCase("other", true, "terminal_search_pinyin_mode", typeof(PinyinSearchMode), true, 1, true, true)]
    [TestCase("digital_storage", false, "terminal_search_pinyin_mode", typeof(PinyinSearchMode), true, 1, true, true)]
    [TestCase("digital_storage", true, "wrong_key", typeof(PinyinSearchMode), true, 1, true, true)]
    [TestCase("digital_storage", true, "terminal_search_pinyin_mode", typeof(string), true, 1, true, true)]
    [TestCase("digital_storage", true, "terminal_search_pinyin_mode", typeof(PinyinSearchMode), false, 1, true, true)]
    [TestCase("digital_storage", true, "terminal_search_pinyin_mode", typeof(PinyinSearchMode), true, -1, true, true)]
    [TestCase("digital_storage", true, "terminal_search_pinyin_mode", typeof(PinyinSearchMode), true, 3, true, true)]
    [TestCase("digital_storage", true, "terminal_search_pinyin_mode", typeof(PinyinSearchMode), true, 1, false, true)]
    [TestCase("digital_storage", true, "terminal_search_pinyin_mode", typeof(PinyinSearchMode), true, 1, true, false)]
    public void InvalidContextDoesNotTouchInjectedUi(string modId, bool windowAlive, string key, Type type,
        bool choicesValid, int index, bool elementAlive, bool dropdownPresent)
    {
        var mutations = 0;
        var choices = choicesValid ? new[] { "Auto", "On", "Off" } : new[] { "On", "Auto", "Off" };
        var applied = PinyinDropdownDisplay.Apply(modId, windowAlive, key, type, choices, index,
            new[] { "自動", "開", "關" }, elementAlive, dropdownPresent,
            _ => mutations++, _ => mutations++, () => mutations++, _ => mutations++);

        Assert.That(applied, Is.False);
        Assert.That(mutations, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void LocalizedLabelsPreserveIndexAndCanonicalChoiceOrder(int index)
    {
        var mutations = new List<string>();
        var nativeChoices = new[] { "Auto", "On", "Off" };
        var selected = index;
        var localized = new[] { "自動", "開啟", "關閉" };

        var result = PinyinDropdownDisplay.Apply("digital_storage", true, "terminal_search_pinyin_mode",
            typeof(PinyinSearchMode), nativeChoices, selected, localized, true, true,
            options => mutations.Add("options:" + string.Join("|", options)), value =>
            {
                selected = value;
                mutations.Add("index:" + value);
            }, () => mutations.Add("refresh"), _ => Assert.Fail("unexpected log"));

        Assert.That(result, Is.True);
        Assert.That(selected, Is.EqualTo(index));
        Assert.That(nativeChoices, Is.EqualTo(new[] { "Auto", "On", "Off" }));
        Assert.That(mutations[0], Is.EqualTo("options:自動|開啟|關閉"));
        Assert.That(mutations[1], Is.EqualTo("index:" + index));
    }

    [Test]
    public void MutationFailureRestoresCanonicalOptionsAndIndexAndLogsOnce()
    {
        var operations = new List<string>();
        var logs = 0;
        var localizedSet = false;
        Action<string[]> setOptions = options =>
        {
            operations.Add("options:" + string.Join("|", options));
            if (options[0] == "自動")
            {
                localizedSet = true;
                throw new InvalidOperationException("fake UI failure");
            }
        };
        Action<int> setIndex = index => operations.Add("index:" + index);
        Action refresh = () => operations.Add("refresh");
        Action<Exception> log = _ => logs++;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            PinyinDropdownDisplay.Apply("digital_storage", true, "terminal_search_pinyin_mode",
                typeof(PinyinSearchMode), new[] { "Auto", "On", "Off" }, 2,
                new[] { "自動", "開啟", "關閉" }, true, true, setOptions, setIndex, refresh, log);
        }

        Assert.That(localizedSet, Is.True);
        Assert.That(operations, Does.Contain("options:Auto|On|Off"));
        Assert.That(operations, Does.Contain("index:2"));
        Assert.That(logs, Is.EqualTo(1));
    }
}
