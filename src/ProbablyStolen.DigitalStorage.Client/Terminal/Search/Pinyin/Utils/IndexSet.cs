using System;
using System.Collections.Generic;
using System.Text;

namespace PininSharp.Utils;

internal readonly struct IndexSet
{
    public static IndexSet Zero => new(new[] { 1 });
    public static IndexSet One => new(new[] { 2 });
    public static IndexSet None => new(Array.Empty<int>());
    public static IndexSet Null => new(null);
    private readonly int[]? _bits;
    private IndexSet(int[]? bits) => _bits = bits;
    public IndexSet(in IndexSet set) => _bits = set._bits;
    public IndexSet(int value) => _bits = value == 0 ? Array.Empty<int>() : new[] { value };
    public IndexSet Set(int index)
    {
        if (index < 0) return this;
        var word = index / 32; var copy = CopyWords(word + 1);
        copy[word] |= 1 << (index % 32);
        return new IndexSet(copy);
    }
    public bool Get(int index) => index >= 0 && _bits != null && index / 32 < _bits.Length &&
        (_bits[index / 32] & (1 << (index % 32))) != 0;
    public IndexSet Merge(in IndexSet other)
    {
        if (IsEmpty()) return other;
        if (other.IsEmpty()) return this;
        if (IsNull()) return other;
        if (other.IsNull()) return this;
        var copy = CopyWords(Math.Max(_bits!.Length, other._bits!.Length));
        for (var i = 0; i < other._bits.Length; i++) copy[i] |= other._bits[i];
        return new IndexSet(copy);
    }
    public bool Traverse(Func<int, bool> visitor)
    {
        if (_bits == null) return false;
        for (var word = 0; word < _bits.Length; word++)
        {
            var value = _bits[word];
            for (var bit = 0; value != 0 && bit < 32; bit++, value >>= 1)
                if ((value & 1) != 0 && visitor(word * 32 + bit)) return true;
        }
        return false;
    }
    public IEnumerable<int> GetEnumerator()
    {
        var values = new List<int>(); Traverse(index => { values.Add(index); return false; }); return values;
    }
    public IndexSet Offset(int offset)
    {
        if (offset <= 0 || IsEmpty() || IsNull()) return this;
        var result = None;
        foreach (var index in GetEnumerator()) result = result.Set(index + offset);
        return result;
    }
    public bool IsNull() => _bits == null;
    public bool IsEmpty() => _bits != null && _bits.Length == 0;
    public IndexSet Copy() => _bits == null ? Null : new IndexSet((int[])_bits.Clone());
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var index in GetEnumerator()) builder.Append(index).Append(", ");
        if (builder.Length == 0) return "0";
        return builder.ToString(0, builder.Length - 2);
    }
    private int[] CopyWords(int length)
    {
        var copy = new int[length];
        if (_bits != null) Array.Copy(_bits, copy, Math.Min(_bits.Length, length));
        return copy;
    }
    internal sealed class Storage
    {
        private IndexSet[] _data = new IndexSet[16];
        private bool[] _set = new bool[16];
        internal void Set(IndexSet value, int index)
        {
            if (index >= _data.Length)
            {
                var length = _data.Length;
                while (length <= index) length *= 2;
                Array.Resize(ref _data, length); Array.Resize(ref _set, length);
            }
            _data[index] = value; _set[index] = true;
        }
        internal IndexSet Get(int index) => index >= 0 && index < _data.Length && _set[index] ? _data[index] : Null;
    }
}
