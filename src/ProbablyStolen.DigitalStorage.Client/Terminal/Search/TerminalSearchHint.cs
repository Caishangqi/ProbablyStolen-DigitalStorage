using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Search;

/// <summary>One line of a search box hint: a text key and the prefix it describes, if any.</summary>
public readonly struct SearchHintLine
{
    internal SearchHintLine(string key, char? prefix)
    {
        Key = key;
        Prefix = prefix;
    }

    /// <summary>Gets the text key; prefix and operator lines take their character as their only argument.</summary>
    public string Key { get; }

    /// <summary>Gets the prefix or operator character the line describes, or null.</summary>
    public char? Prefix { get; }
}

/// <summary>
/// The syntax lines shown when hovering a search box: the prefixes whose fields are switched on, the operators, and
/// pinyin while it is on. Prefixes of fields without a known description are left out.
/// </summary>
public static class TerminalSearchHint
{
    /// <summary>The line telling that a right click clears the box.</summary>
    public const string ClearKey = "terminal_search_tip_clear";

    /// <summary>The line telling that pinyin matches Chinese text.</summary>
    public const string PinyinKey = "terminal_search_tip_pinyin";

    private static readonly IReadOnlyDictionary<string, string> PrefixKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tags"] = "terminal_search_tip_tags",
        ["types"] = "terminal_search_tip_types",
        ["descriptions"] = "terminal_search_tip_descriptions",
        ["identifier"] = "terminal_search_tip_identifier"
    };

    // The characters come from code, so the localized text never has to escape a quote.
    private static readonly (string Key, char Operator)[] Operators =
    {
        ("terminal_search_tip_negate", '-'), ("terminal_search_tip_or", '|'), ("terminal_search_tip_quote", '"')
    };

    /// <summary>Lists the hint lines of the terminal search under its current settings.</summary>
    /// <param name="search">The search service.</param>
    /// <param name="syntax">Whether the box accepts prefixes and operators; plain name filters list only pinyin.</param>
    public static IReadOnlyList<SearchHintLine> Lines(TerminalSearchService search, bool syntax)
    {
        if (search == null) throw new ArgumentNullException(nameof(search));
        var lines = new List<SearchHintLine>();
        if (syntax)
        {
            foreach (var pair in search.ActivePrefixList())
                if (PrefixKeys.TryGetValue(pair.Value, out var key)) lines.Add(new SearchHintLine(key, pair.Key));
            foreach (var (key, character) in Operators) lines.Add(new SearchHintLine(key, character));
        }

        if (search.Configuration.PinyinEnabled) lines.Add(new SearchHintLine(PinyinKey, null));
        lines.Add(new SearchHintLine(ClearKey, null));
        return lines;
    }
}
