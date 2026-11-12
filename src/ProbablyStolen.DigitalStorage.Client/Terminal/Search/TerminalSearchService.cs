using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Search;

/// <summary>Controls whether a searchable field participates in unprefixed queries.</summary>
public enum SearchMode
{
    /// <summary>Search the field both with its prefix and in ordinary queries.</summary>
    Enabled,
    /// <summary>Search the field only when its registered prefix is present.</summary>
    RequirePrefix,
    /// <summary>Do not search the field, including when its prefix is present.</summary>
    Disabled
}

/// <summary>Selects the pinyin keyboard mapping used to encode syllables.</summary>
public enum PinyinKeyboard
{
    /// <summary>Full pinyin, including optional tone numbers.</summary>
    Quanpin,
    /// <summary>Daqian phonetic keyboard.</summary>
    Daqian,
    /// <summary>Xiaohe double pinyin.</summary>
    Xiaohe,
    /// <summary>Ziranma double pinyin.</summary>
    Ziranma,
    /// <summary>Sougou double pinyin.</summary>
    Sougou,
    /// <summary>Guobiao double pinyin.</summary>
    Guobiao,
    /// <summary>Microsoft double pinyin.</summary>
    Microsoft,
    /// <summary>PinyinPP double pinyin.</summary>
    PinyinPp,
    /// <summary>Ziguang double pinyin.</summary>
    Ziguang
}

/// <summary>Immutable pinyin, fuzzy matching, field-mode and syntax-highlighting options.</summary>
public sealed class SearchConfiguration
{
    /// <summary>Creates options using full pinyin and the approved terminal field defaults.</summary>
    public SearchConfiguration(PinyinKeyboard keyboard = PinyinKeyboard.Quanpin,
        bool enabled = true, bool zh2z = false, bool sh2s = false, bool ch2c = false,
        bool ang2an = false, bool ing2in = false, bool eng2en = false, bool u2v = false,
        SearchMode tags = SearchMode.RequirePrefix, SearchMode itemTypes = SearchMode.RequirePrefix,
        SearchMode descriptions = SearchMode.Enabled, SearchMode identifiers = SearchMode.Disabled,
        bool syntaxHighlighting = true)
    {
        Keyboard = keyboard; PinyinEnabled = enabled; Zh2Z = zh2z; Sh2S = sh2s; Ch2C = ch2c;
        Ang2An = ang2an; Ing2In = ing2in; Eng2En = eng2en; U2V = u2v;
        Tags = tags; ItemTypes = itemTypes; Descriptions = descriptions; Identifiers = identifiers;
        SyntaxHighlighting = syntaxHighlighting;
    }

    /// <summary>Gets the pinyin keyboard layout.</summary>
    public PinyinKeyboard Keyboard { get; }
    /// <summary>Gets whether pinyin matching is active.</summary>
    public bool PinyinEnabled { get; }
    /// <summary>Gets whether zh and z may match each other.</summary>
    public bool Zh2Z { get; }
    /// <summary>Gets whether sh and s may match each other.</summary>
    public bool Sh2S { get; }
    /// <summary>Gets whether ch and c may match each other.</summary>
    public bool Ch2C { get; }
    /// <summary>Gets whether ang and an may match each other.</summary>
    public bool Ang2An { get; }
    /// <summary>Gets whether ing and in may match each other.</summary>
    public bool Ing2In { get; }
    /// <summary>Gets whether eng and en may match each other.</summary>
    public bool Eng2En { get; }
    /// <summary>Gets whether u and v may match each other.</summary>
    public bool U2V { get; }
    /// <summary>Gets the unprefixed-search mode for tag names.</summary>
    public SearchMode Tags { get; }
    /// <summary>Gets the unprefixed-search mode for item types.</summary>
    public SearchMode ItemTypes { get; }
    /// <summary>Gets the unprefixed-search mode for descriptions.</summary>
    public SearchMode Descriptions { get; }
    /// <summary>Gets the unprefixed-search mode for identifiers.</summary>
    public SearchMode Identifiers { get; }
    /// <summary>Gets whether syntax color spans are produced.</summary>
    public bool SyntaxHighlighting { get; }
}

/// <summary>Detached searchable strings captured from one terminal inventory sample.</summary>
public sealed class SearchDocument
{
    /// <summary>Creates a document without retaining native item or inventory objects.</summary>
    public SearchDocument(string name, string identifier = "", IEnumerable<string>? tagNames = null,
        IEnumerable<string>? itemTypes = null, IEnumerable<string>? descriptions = null,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? addonFields = null)
    {
        Name = name ?? string.Empty; Identifier = identifier ?? string.Empty;
        TagNames = Copy(tagNames); ItemTypes = Copy(itemTypes); Descriptions = Copy(descriptions);
        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var field in addonFields ?? Array.Empty<KeyValuePair<string, IEnumerable<string>>>())
        {
            if (string.IsNullOrWhiteSpace(field.Key)) throw new ArgumentException("Field id is required.", nameof(addonFields));
            if (!fields.TryAdd(field.Key, Copy(field.Value))) throw new ArgumentException("Duplicate field id: " + field.Key, nameof(addonFields));
        }
        AddonFields = new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(fields);
    }

    /// <summary>Gets the localized display name, which is always searchable.</summary>
    public string Name { get; }
    /// <summary>Gets the item identifier.</summary>
    public string Identifier { get; }
    /// <summary>Gets enabled tag keys and display names, never tag values.</summary>
    public IReadOnlyList<string> TagNames { get; }
    /// <summary>Gets item type labels and keys.</summary>
    public IReadOnlyList<string> ItemTypes { get; }
    /// <summary>Gets short, long and flavor descriptions.</summary>
    public IReadOnlyList<string> Descriptions { get; }
    /// <summary>Gets immutable, detached addon-provided string fields.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AddonFields { get; }

    /// <summary>Compares all captured strings by ordinal value and order.</summary>
    public bool HasSameContent(SearchDocument other) => other != null &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        string.Equals(Identifier, other.Identifier, StringComparison.Ordinal) &&
        TagNames.SequenceEqual(other.TagNames, StringComparer.Ordinal) &&
        ItemTypes.SequenceEqual(other.ItemTypes, StringComparer.Ordinal) &&
        Descriptions.SequenceEqual(other.Descriptions, StringComparer.Ordinal) &&
        AddonFields.Count == other.AddonFields.Count && AddonFields.All(pair =>
            other.AddonFields.TryGetValue(pair.Key, out var values) && pair.Value.SequenceEqual(values, StringComparer.Ordinal));

    private static IReadOnlyList<string> Copy(IEnumerable<string>? values) =>
        Array.AsReadOnly((values ?? Array.Empty<string>()).Where(value => !string.IsNullOrEmpty(value)).ToArray());
}

/// <summary>Classifies a UTF16 range in the raw query for UI coloring.</summary>
public enum SearchColorKind
{
    /// <summary>Ordinary unprefixed query text.</summary>
    Ordinary,
    /// <summary>A recognized prefix character.</summary>
    Prefix,
    /// <summary>Text selected by a recognized prefix.</summary>
    PrefixedText,
    /// <summary>An exclusion or OR operator.</summary>
    Operator,
    /// <summary>Query text when the complete query has no matches.</summary>
    NoMatch
}

/// <summary>One raw UTF16 query range and its syntax color class.</summary>
public readonly struct SearchColorSpan
{
    /// <summary>Creates a color range.</summary>
    public SearchColorSpan(int start, int length, SearchColorKind kind) { Start = start; Length = length; Kind = kind; }
    /// <summary>Gets the first UTF16 code unit.</summary>
    public int Start { get; }
    /// <summary>Gets the number of UTF16 code units.</summary>
    public int Length { get; }
    /// <summary>Gets the color class.</summary>
    public SearchColorKind Kind { get; }
}

/// <summary>Provides detached strings and their mode for an explicitly registered search field.</summary>
public sealed class SearchFieldProvider
{
    /// <summary>Creates a field provider used by the terminal search registry.</summary>
    public SearchFieldProvider(string id, Func<SearchDocument, IEnumerable<string>> values,
        Func<SearchConfiguration, SearchMode> mode)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Field id is required.", nameof(id));
        Id = id;
        Values = values ?? throw new ArgumentNullException(nameof(values));
        Mode = mode ?? throw new ArgumentNullException(nameof(mode));
    }
    /// <summary>Gets the stable field id referenced by prefixes.</summary>
    public string Id { get; }
    /// <summary>Gets detached terms from a document.</summary>
    public Func<SearchDocument, IEnumerable<string>> Values { get; }
    /// <summary>Gets the field mode for a configuration.</summary>
    public Func<SearchConfiguration, SearchMode> Mode { get; }
}

/// <summary>Explicit registration point for search matchers, fields and prefix providers.</summary>
public sealed class SearchMatcherRegistry
{
    private readonly Dictionary<string, Func<string, string, bool>> _matchers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SearchFieldProvider> _fields = new(StringComparer.Ordinal);
    private readonly Dictionary<char, string> _prefixes = new();
    internal Func<string, string, bool>? Matcher { get; private set; }
    internal IReadOnlyDictionary<string, SearchFieldProvider> Fields => _fields;
    internal IReadOnlyDictionary<char, string> Prefixes => _prefixes;
    private readonly Dictionary<string, Func<SearchDocument, IEnumerable<string>>> _documentFields = new(StringComparer.Ordinal);
    private bool _sealed;

    internal void Seal() => _sealed = true;

    private void EnsureOpen()
    {
        if (_sealed) throw new InvalidOperationException("Search registration has already completed.");
    }

    /// <summary>Registers a field captured from detached metadata once per inventory snapshot.</summary>
    public void RegisterDocumentField(string id, Func<SearchDocument, IEnumerable<string>> capture,
        Func<SearchConfiguration, SearchMode> mode)
    {
        EnsureOpen();
        if (capture == null) throw new ArgumentNullException(nameof(capture));
        RegisterField(new SearchFieldProvider(id,
            document => document.AddonFields.TryGetValue(id, out var values) ? values : Array.Empty<string>(), mode));
        _documentFields.Add(id, capture);
    }

    internal SearchDocument PrepareDocument(SearchDocument document)
    {
        if (_documentFields.Count == 0) return document;
        var fields = document.AddonFields.Where(pair => !_documentFields.ContainsKey(pair.Key))
            .Select(pair => new KeyValuePair<string, IEnumerable<string>>(pair.Key, pair.Value)).ToList();
        foreach (var pair in _documentFields)
            fields.Add(new KeyValuePair<string, IEnumerable<string>>(pair.Key, pair.Value(document)));
        return new SearchDocument(document.Name, document.Identifier, document.TagNames, document.ItemTypes,
            document.Descriptions, fields);
    }

    /// <summary>Registers a matcher id, rejecting duplicate ids.</summary>
    public void RegisterMatcher(string id, Func<string, string, bool> matcher)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Matcher id is required.", nameof(id));
        if (matcher == null) throw new ArgumentNullException(nameof(matcher));
        if (!_matchers.TryAdd(id, matcher)) throw new InvalidOperationException("Duplicate search matcher id: " + id);
        Matcher = (source, query) => _matchers.Values.Any(candidate => candidate(source, query));
    }

    /// <summary>Registers one searchable document field by stable id.</summary>
    public void RegisterField(SearchFieldProvider provider)
    {
        EnsureOpen();
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (!_fields.TryAdd(provider.Id, provider)) throw new InvalidOperationException("Duplicate search field id: " + provider.Id);
    }

    /// <summary>Associates a punctuation prefix with a previously registered field.</summary>
    public void RegisterPrefix(char prefix, string fieldId)
    {
        EnsureOpen();
        if (!char.IsPunctuation(prefix) && !char.IsSymbol(prefix) || prefix == '-' || prefix == '|' || prefix == '"' || prefix == '\\') throw new ArgumentException("Prefix must be a punctuation character.", nameof(prefix));
        if (string.IsNullOrWhiteSpace(fieldId)) throw new ArgumentException("Field id is required.", nameof(fieldId));
        if (!_fields.ContainsKey(fieldId)) throw new InvalidOperationException("Unknown search field id: " + fieldId);
        if (!_prefixes.TryAdd(prefix, fieldId)) throw new InvalidOperationException("Duplicate search prefix: " + prefix);
    }
}

/// <summary>Matches terminal documents using registered pinyin, field and prefix providers.</summary>
public sealed class TerminalSearchService
{
    /// <summary>Maps a rendered UTF16 index to committed text, excluding an inserted IME composition.</summary>
    public static int CommittedIndex(int renderedIndex, int insertionIndex, int compositionLength)
    {
        if (renderedIndex < insertionIndex) return renderedIndex;
        return renderedIndex < insertionIndex + compositionLength ? -1 : renderedIndex - compositionLength;
    }

    /// <summary>JEI-compatible ARGB value for recognized prefix characters.</summary>
    public const uint PrefixArgb = 0xFF55FFFF;
    /// <summary>JEI-compatible ARGB value for text selected by a prefix.</summary>
    public const uint PrefixedArgb = 0xFF55FF55;
    /// <summary>JEI-compatible ARGB value for query operators.</summary>
    public const uint OperatorArgb = 0xFFFFAA00;
    /// <summary>JEI-compatible ARGB value for ordinary query text.</summary>
    public const uint OrdinaryArgb = 0xFFFFFFFF;
    /// <summary>JEI-compatible ARGB value for a query with no matches.</summary>
    public const uint NoMatchArgb = 0xFFFF0000;
    private readonly SearchMatcherRegistry _registry;
    private SearchConfiguration _configuration;
    private string? _queryText;
    private ParsedSearchQuery? _parsedQuery;

    /// <summary>Creates a service from explicit registrations and current options.</summary>
    public TerminalSearchService(SearchMatcherRegistry registry, SearchConfiguration configuration)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _registry.Seal();
    }

    /// <summary>Captures registered addon fields before the terminal filters a detached snapshot.</summary>
    public SearchDocument PrepareDocument(SearchDocument document) =>
        _registry.PrepareDocument(document ?? throw new ArgumentNullException(nameof(document)));

    /// <summary>Gets the active configuration.</summary>
    public SearchConfiguration Configuration => _configuration;
    /// <summary>Gets the monotonically increasing configuration revision.</summary>
    public long ConfigurationRevision { get; private set; }
    /// <summary>Replaces configuration without retaining inventory or query state.</summary>
    public void UpdateConfiguration(SearchConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ConfigurationRevision++;
        _parsedQuery = null;
    }

    /// <summary>Checks a document against AND terms and OR groups in the query.</summary>
    public bool Matches(SearchDocument document, string? query)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        return Matches(document, ParseQuery(query ?? string.Empty), MatchText);
    }

    // Keep memoization within one filter pass: external addons may change between accepted snapshots.
    internal Func<SearchDocument, bool> CreatePredicate(string query)
    {
        var parsed = ParseQuery(query);
        var cache = new Dictionary<(string Source, string Query), bool>();
        var order = new Queue<(string Source, string Query)>();
        bool MatchCached(string source, string term)
        {
            var key = (source, term);
            if (cache.TryGetValue(key, out var found)) return found;
            var result = MatchText(source, term);
            if (cache.Count == 4096) cache.Remove(order.Dequeue());
            cache.Add(key, result);
            order.Enqueue(key);
            return result;
        }
        return document => Matches(document, parsed, MatchCached);
    }

    private bool Matches(SearchDocument document, ParsedSearchQuery parsed, Func<string, string, bool> matchText)
    {
        if (parsed.Groups.Count == 0) return true;
        return parsed.Groups.Any(group => group.All(term => term.Value.Length == 0
            ? false
            : term.Negated ? !TermMatches(document, term, matchText) : TermMatches(document, term, matchText)));
    }

    /// <summary>
    /// Checks one plain label against typed text as the terminal matches a name: a case-insensitive substring, or a
    /// registered matcher such as pinyin while it is enabled. The query syntax (prefixes, negation, alternatives) does
    /// not apply, so short lists such as dropdown choices filter by what was typed.
    /// </summary>
    public bool MatchesText(string text, string query) =>
        MatchText(text ?? string.Empty, (query ?? string.Empty).Trim());

    /// <summary>Tokenizes the same query syntax into raw UTF16 color spans.</summary>
    public IReadOnlyList<SearchColorSpan> GetColorSpans(string? query, bool matched)
    {
        var text = query ?? string.Empty;
        if (!matched && text.Length > 0) return new[] { new SearchColorSpan(0, text.Length, SearchColorKind.NoMatch) };
        return ParseQuery(text).Spans;
    }

    private ParsedSearchQuery ParseQuery(string text)
    {
        if (_parsedQuery == null || !string.Equals(_queryText, text, StringComparison.Ordinal))
        {
            _parsedQuery = SearchQuery.Parse(text, ActivePrefixes());
            _queryText = text;
        }
        return _parsedQuery;
    }

    private IReadOnlyDictionary<char, string> ActivePrefixes()
    {
        var result = new Dictionary<char, string>();
        foreach (var pair in _registry.Prefixes)
        {
            if (_registry.Fields.TryGetValue(pair.Value, out var provider) && provider.Mode(_configuration) != SearchMode.Disabled)
                result.Add(pair.Key, pair.Value);
        }
        return result;
    }

    private bool TermMatches(SearchDocument document, SearchTerm term, Func<string, string, bool> matchText)
    {
        if (term.FieldId == null)
        {
            if (matchText(document.Name, term.Value)) return true;
            foreach (var provider in _registry.Fields.Values)
                if (provider.Mode(_configuration) == SearchMode.Enabled && Any(provider.Values(document))) return true;
            return false;
        }
        if (term.EmptyPrefix || !_registry.Fields.TryGetValue(term.FieldId, out var field) ||
            field.Mode(_configuration) == SearchMode.Disabled) return false;
        return Any(field.Values(document));

        bool Any(IEnumerable<string> values) => values.Any(value => matchText(value, term.Value));
    }

    private bool MatchText(string value, string query) => !string.IsNullOrEmpty(query) &&
        (value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (_registry.Matcher != null && _registry.Matcher(value, query)));
}

/// <summary>Owns the pinyin dictionary matcher registered with a search registry.</summary>
public sealed class PinyinSearchAddon : IDisposable
{
    private PininSharp.PinyinMatcher? _matcher;
    private SearchConfiguration _configuration;
    private SearchConfiguration? _appliedConfiguration;
    private readonly Action? _onDictionaryLoad;
    private bool _disposed;

    /// <summary>Registers an initially unavailable matcher without reading resources during mod initialization.</summary>
    public PinyinSearchAddon(SearchConfiguration configuration) =>
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    internal PinyinSearchAddon(SearchConfiguration configuration, Action onDictionaryLoad) : this(configuration) =>
        _onDictionaryLoad = onDictionaryLoad ?? throw new ArgumentNullException(nameof(onDictionaryLoad));

    /// <summary>Loads a dictionary and prepares the selected keyboard and fuzzy rules.</summary>
    public PinyinSearchAddon(string dictionary, SearchConfiguration configuration) : this(configuration) =>
        LoadDictionary(dictionary);

    /// <summary>Loads dictionary text after its owning platform reports that mod resources are ready.</summary>
    public void LoadDictionary(string dictionary)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PinyinSearchAddon));
        var matcher = new PininSharp.PinyinMatcher(dictionary ?? throw new ArgumentNullException(nameof(dictionary)),
            _configuration, _onDictionaryLoad);
        _matcher = matcher;
        _appliedConfiguration = _configuration;
    }

    /// <summary>Applies options immediately, or retains them until the dictionary is available.</summary>
    public void Configure(SearchConfiguration configuration)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PinyinSearchAddon));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (configuration.PinyinEnabled && _matcher != null &&
            (_appliedConfiguration == null || !SamePinyinOptions(_appliedConfiguration, configuration)))
        {
            _matcher.Configure(configuration);
            _appliedConfiguration = configuration;
        }
        _configuration = configuration;
    }

    private static bool SamePinyinOptions(SearchConfiguration left, SearchConfiguration right) =>
        left.Keyboard == right.Keyboard && left.Zh2Z == right.Zh2Z && left.Sh2S == right.Sh2S &&
        left.Ch2C == right.Ch2C && left.Ang2An == right.Ang2An && left.Ing2In == right.Ing2In &&
        left.Eng2En == right.Eng2En && left.U2V == right.U2V;

    /// <summary>Checks pinyin only while enabled and after a dictionary has loaded.</summary>
    public bool Matches(string source, string query) => !_disposed && _configuration.PinyinEnabled &&
        _matcher != null && _matcher.Contains(source ?? string.Empty, query ?? string.Empty);

    /// <summary>Makes a registered matcher inert and releases its dictionary state.</summary>
    public void Dispose()
    {
        _disposed = true;
        _matcher = null;
        _appliedConfiguration = null;
    }
}

internal sealed class SearchTerm
{
    internal SearchTerm(string value, bool negated, string? fieldId, bool emptyPrefix) { Value = value; Negated = negated; FieldId = fieldId; EmptyPrefix = emptyPrefix; }
    internal string Value { get; }
    internal bool Negated { get; }
    internal string? FieldId { get; }
    internal bool EmptyPrefix { get; }
}

internal sealed class ParsedSearchQuery
{
    internal ParsedSearchQuery(IReadOnlyList<IReadOnlyList<SearchTerm>> groups, IReadOnlyList<SearchColorSpan> spans) { Groups = groups; Spans = spans; }
    internal IReadOnlyList<IReadOnlyList<SearchTerm>> Groups { get; }
    internal IReadOnlyList<SearchColorSpan> Spans { get; }
}

internal static class SearchQuery
{
    internal static ParsedSearchQuery Parse(string raw, IReadOnlyDictionary<char, string> prefixes)
    {
        var groups = new List<IReadOnlyList<SearchTerm>>(); var terms = new List<SearchTerm>(); var spans = new List<SearchColorSpan>();
        var text = new System.Text.StringBuilder(); var start = 0; var quoted = false; var escaped = false; var negated = false; string? field = null; var empty = false; var prefixStart = -1; var quoteStart = -1;
        var quotedRanges = new List<(int Start, int End)>();
        void Finish(int end)
        {
            if (text.Length == 0 && !empty && !negated) return;
            terms.Add(new SearchTerm(text.ToString(), negated, field, empty));
            if (field != null && prefixStart >= 0)
            {
                spans.Add(new SearchColorSpan(prefixStart, 1, SearchColorKind.Prefix));
                var segmentStart = -1;
                var rangeIndex = 0;
                for (var index = prefixStart + 1; index < end; index++)
                {
                    while (rangeIndex < quotedRanges.Count && index >= quotedRanges[rangeIndex].End) rangeIndex++;
                    var isQuoted = raw[index] == '"' || (rangeIndex < quotedRanges.Count && index >= quotedRanges[rangeIndex].Start);
                    if (!isQuoted && segmentStart < 0) segmentStart = index;
                    if ((isQuoted || index == end - 1) && segmentStart >= 0)
                    {
                        var segmentEnd = isQuoted ? index : index + 1;
                        spans.Add(new SearchColorSpan(segmentStart, segmentEnd - segmentStart, SearchColorKind.PrefixedText));
                        segmentStart = -1;
                    }
                }
            }
            text.Clear(); field = null; empty = false; negated = false; prefixStart = -1; quoteStart = -1; quotedRanges.Clear(); start = end + 1;
        }
        for (var i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (escaped) { text.Append(c); escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '"')
            {
                if (!quoted) quoteStart = i + 1;
                else quotedRanges.Add((quoteStart, i));
                quoted = !quoted;
                continue;
            }
            if (!quoted && char.IsWhiteSpace(c)) { Finish(i); continue; }
            if (!quoted && c == '|') { Finish(i); if (terms.Count > 0) groups.Add(terms.ToArray()); terms.Clear(); spans.Add(new SearchColorSpan(i, 1, SearchColorKind.Operator)); start = i + 1; continue; }
            if (!quoted && text.Length == 0 && field == null && !negated && c == '-') { negated = true; spans.Add(new SearchColorSpan(i, 1, SearchColorKind.Operator)); start = i + 1; continue; }
            if (!quoted && text.Length == 0 && field == null && prefixes.TryGetValue(c, out var id)) { field = id; prefixStart = i; empty = true; continue; }
            text.Append(c); empty = false;
        }
        if (quoted) quotedRanges.Add((quoteStart, raw.Length));
        if (escaped) text.Append('\\');
        Finish(raw.Length);
        if (terms.Count > 0) groups.Add(terms.ToArray());
        return new ParsedSearchQuery(groups, spans);
    }
}
