using System;
using System.Collections.Generic;
using System.Linq;
using PininSharp.Elements;
using PininSharp.Utils;

namespace PininSharp;

internal sealed class PinIn
{
    private int _total;
    private readonly Cache<string, Phoneme> _phonemes;
    private readonly Cache<string, Pinyin> _pinyins;
    private readonly Character?[] _chars = new Character?[char.MaxValue + 1];
    private readonly Accelerator _accelerator;

    internal Keyboard Keyboard { get; set; } = Keyboard.Quanpin;
    internal bool FZh2Z { get; set; }
    internal bool FSh2S { get; set; }
    internal bool FCh2C { get; set; }
    internal bool FAng2An { get; set; }
    internal bool FIng2In { get; set; }
    internal bool FEng2En { get; set; }
    internal bool FU2V { get; set; }
    internal bool Accelerate { get; set; }
    internal PinyinFormat FormatMode = PinyinFormat.Number;
    private readonly IDictLoader _loader;

    internal PinIn(IDictLoader loader)
    {
        _phonemes = new Cache<string, Phoneme>(s => new Phoneme(s, this));
        _pinyins = new Cache<string, Pinyin>(s => new Pinyin(s, this, _total++));
        _accelerator = new Accelerator(this);
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    internal void Configure(PinyinKeyboardOptions options)
    {
        Keyboard = options.Keyboard;
        FZh2Z = options.Zh2Z; FSh2S = options.Sh2S; FCh2C = options.Ch2C;
        FAng2An = options.Ang2An; FIng2In = options.Ing2In; FEng2En = options.Eng2En; FU2V = options.U2V;
        _phonemes.Clear(); _pinyins.Clear(); _total = 0; _accelerator.Reset();
        Array.Clear(_chars, 0, _chars.Length);
        Load();
    }

    internal void Load()
    {
        _loader.Load((character, readings) =>
        {
            if (readings == null)
            {
                _chars[character] = null;
                return;
            }

            var pinyins = readings.Select(GetPinyin).Where(pinyin => pinyin.HasValue)
                .Select(pinyin => pinyin!.Value).ToArray();
            _chars[character] = new Character(character, pinyins);
        });
    }

    internal bool Contains(string source, string query)
    {
        if (!Accelerate) return Matcher.Contains(source, query, this);
        _accelerator.SetProvider(source);
        _accelerator.Search(query);
        return _accelerator.Contains(0, 0);
    }

    internal bool Begins(string source, string query)
    {
        if (!Accelerate) return Matcher.Begins(source, query, this);
        _accelerator.SetProvider(source);
        _accelerator.Search(query);
        return _accelerator.Begins(0, 0);
    }

    internal bool Matches(string source, string query)
    {
        if (Accelerate)
        {
            _accelerator.SetProvider(source);
            _accelerator.Search(query);
            return _accelerator.Matches(0, 0);
        }

        return Matcher.Matches(source, query, this);
    }

    internal Phoneme? GetPhoneme(string value) => _phonemes.Get(value);

    internal Pinyin? GetPinyin(string value) => _pinyins.Get(value);

    internal Character GetCharacter(char value) => _chars[value] ?? new Character(value, Array.Empty<Pinyin>());

    internal string FormatPinyin(ref Pinyin pinyin) => FormatMode.Format(ref pinyin);

    internal static class Matcher
    {
        internal static bool Begins(string source, string query, PinIn context)
        {
            if (source.Trim().Length == 0) return source.StartsWith(query, StringComparison.Ordinal);
            return Check(source, query, context, true, false);
        }

        internal static bool Contains(string source, string query, PinIn context)
        {
            if (source.Trim().Length == 0) return source.Contains(query, StringComparison.Ordinal);
            return Check(source, query, context, true, true);
        }

        internal static bool Matches(string source, string query, PinIn context)
        {
            if (source.Trim().Length == 0) return source == query;
            return Check(source, query, context, false, false);
        }

        private static bool Check(string source, string query, PinIn context, bool partial, bool contains)
        {
            if (query.Length == 0) return partial || source.Length == 0;
            // Each bit is a reachable query offset. Repeated source characters reuse transition masks;
            // advancing a source character shifts whole words instead of revisiting every active offset.
            var wordCount = query.Length / 64 + 1;
            var current = new ulong[wordCount];
            var next = new ulong[wordCount];
            current[0] = 1;
            var cache = new Dictionary<char, Dictionary<int, ulong[]>>();
            var cacheOrder = new Queue<char>();
            for (var sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (contains) current[0] |= 1;
                var character = source[sourceIndex];
                if (!cache.TryGetValue(character, out var transitions))
                {
                    transitions = new Dictionary<int, ulong[]>();
                    var element = context.GetCharacter(character);
                    for (var index = 0; index < query.Length; index++)
                    {
                        foreach (var length in element.Match(query, index, partial).GetEnumerator())
                        {
                            if (length <= 0 || length > query.Length - index) continue;
                            if (!transitions.TryGetValue(length, out var mask))
                                transitions.Add(length, mask = new ulong[wordCount]);
                            mask[index / 64] |= 1UL << (index % 64);
                        }
                    }
                    // Bound retained masks, without restricting source or query lengths.
                    if (cache.Count == 64) cache.Remove(cacheOrder.Dequeue());
                    cache.Add(character, transitions);
                    cacheOrder.Enqueue(character);
                }
                Array.Clear(next, 0, next.Length);
                foreach (var transition in transitions)
                {
                    var wordShift = transition.Key / 64;
                    var bitShift = transition.Key % 64;
                    for (var word = 0; word + wordShift < wordCount; word++)
                    {
                        var value = current[word] & transition.Value[word];
                        next[word + wordShift] |= value << bitShift;
                        if (bitShift != 0 && word + wordShift + 1 < wordCount)
                            next[word + wordShift + 1] |= value >> (64 - bitShift);
                    }
                }
                if ((next[query.Length / 64] & (1UL << (query.Length % 64))) != 0 &&
                    (partial || sourceIndex == source.Length - 1)) return true;
                var previous = current;
                current = next;
                next = previous;
                if (!contains && current.All(word => word == 0)) return false;
            }
            return false;
        }
    }
}
