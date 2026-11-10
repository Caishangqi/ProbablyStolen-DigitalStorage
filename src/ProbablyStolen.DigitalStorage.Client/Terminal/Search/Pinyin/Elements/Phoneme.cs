using PininSharp.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PininSharp.Elements
{
    internal readonly struct Phoneme : IElement
    {
        private readonly string[] _strings;

        public override string ToString()
        {
            return _strings[0];
        }

        public Phoneme(string str, PinIn p)
        {
            _strings = Array.Empty<string>();

            var ret = new HashSet<string>
            {
                str
            };

            if (p.FCh2C && str.StartsWith("c", StringComparison.Ordinal)) ret.AddAll(new[] { "c", "ch" });
            if (p.FSh2S && str.StartsWith("s", StringComparison.Ordinal)) ret.AddAll(new[] { "s", "sh" });
            if (p.FZh2Z && str.StartsWith("z", StringComparison.Ordinal)) ret.AddAll(new[] { "z", "zh" });
            if (p.FU2V && str.StartsWith("v", StringComparison.Ordinal))
                ret.Add("u" + str.Substring(1));
            if (p.FU2V && str.StartsWith("u", StringComparison.Ordinal))
                ret.Add("v" + str.Substring(1));
            if ((p.FAng2An && str.EndsWith("ang", StringComparison.Ordinal))
                || (p.FEng2En && str.EndsWith("eng", StringComparison.Ordinal))
                || (p.FIng2In && str.EndsWith("ing", StringComparison.Ordinal)))
                ret.Add(str.Substring(0, str.Length - 1));
            if ((p.FAng2An && str.EndsWith("an", StringComparison.Ordinal))
                || (p.FEng2En && str.EndsWith("en", StringComparison.Ordinal))
                || (p.FIng2In && str.EndsWith("in", StringComparison.Ordinal)))
                ret.Add(str + 'g');
            _strings = ret.Select(p.Keyboard.Keys).ToArray();
        }

        public IndexSet Match(string source, IndexSet idx, int start, bool partial)
        {
            if (_strings.Length == 1 && _strings[0].Trim() == "") return new IndexSet(idx);
            var ret = new IndexSet(0);

            foreach (var i in idx.GetEnumerator())
            {
                var set = Match(source, start + i, partial);
                set = set.Offset(i);
                ret = ret.Merge(set);
            }
            return ret;
        }

        public bool MatchSequence(char c)
        {
            return _strings.Any(s => s.Length > 0 && char.ToLowerInvariant(s[0]) == char.ToLowerInvariant(c));
        }

        public bool IsEmpty()
        {
            return _strings.Length == 1 && _strings[0].Trim() == "";
        }

        private static int StrCmp(string a, string b, int aStart)
        {
            if (aStart < 0 || aStart > a.Length) return 0;
            var len = Math.Min(a.Length - aStart, b.Length);
            for (var i = 0; i < len; i++)
                if (char.ToLowerInvariant(a[i + aStart]) != char.ToLowerInvariant(b[i])) return i;
            return len;
        }

        public IndexSet Match(string source, int start, bool partial)
        {
            var ret = new IndexSet(0);
            if (_strings.Length == 1 && _strings[0].Trim() == "") return ret;
            foreach (var str in _strings)
            {
                var size = StrCmp(source, str, start);
                if (partial && start + size == source.Length) ret = ret.Set(size);  // ending match
                else if (size == str.Length) ret = ret.Set(size); // full match
            }
            return ret;
        }

        public override int GetHashCode()
        {
            return _strings.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return Equals(_strings, ((Phoneme)obj)._strings);
        }
    }
}
