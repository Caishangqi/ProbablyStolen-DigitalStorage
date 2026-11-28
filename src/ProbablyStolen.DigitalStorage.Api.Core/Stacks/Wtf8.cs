using System;

namespace ProbablyStolen.DigitalStorage.Api.Stacks;

/// <summary>
/// Lossless text encoding of identity strings. Well-formed text gives the same bytes as UTF-8; a lone surrogate, which
/// item text from other mods may hold, is written as its own three-byte sequence instead of being refused or replaced,
/// so distinct strings never share bytes.
/// </summary>
internal static class Wtf8
{
    /// <summary>Counts the encoded bytes and tells whether the text holds a lone surrogate.</summary>
    internal static int GetByteCount(string value, out bool loneSurrogate)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        loneSurrogate = false;
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c < 0x80) count += 1;
            else if (c < 0x800) count += 2;
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                count += 4;
                i++;
            }
            else
            {
                if (char.IsSurrogate(c)) loneSurrogate = true;
                count += 3;
            }
        }

        return count;
    }

    /// <summary>Encodes the text.</summary>
    internal static byte[] GetBytes(string value)
    {
        var bytes = new byte[GetByteCount(value, out _)];
        var at = 0;
        for (var i = 0; i < value.Length; i++)
        {
            int c = value[i];
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                c = char.ConvertToUtf32(value[i], value[i + 1]);
                i++;
            }

            if (c < 0x80)
            {
                bytes[at++] = (byte)c;
            }
            else if (c < 0x800)
            {
                bytes[at++] = (byte)(0xC0 | (c >> 6));
                bytes[at++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (c < 0x10000)
            {
                bytes[at++] = (byte)(0xE0 | (c >> 12));
                bytes[at++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                bytes[at++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                bytes[at++] = (byte)(0xF0 | (c >> 18));
                bytes[at++] = (byte)(0x80 | ((c >> 12) & 0x3F));
                bytes[at++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                bytes[at++] = (byte)(0x80 | (c & 0x3F));
            }
        }

        return bytes;
    }

    /// <summary>
    /// Decodes bytes written by <see cref="GetBytes"/>, or returns null when they are not its exact output: overlong
    /// forms, values past U+10FFFF and a surrogate pair written as two three-byte sequences are refused, so each string
    /// has one encoding.
    /// </summary>
    internal static string? GetString(byte[] data, int offset, int length)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (offset < 0 || length < 0 || length > data.Length - offset) return null;
        var chars = new char[length];
        var count = 0;
        var end = offset + length;
        var previousHigh = false;
        var i = offset;
        while (i < end)
        {
            var b = data[i];
            int c;
            int size;
            if (b < 0x80) { c = b; size = 1; }
            else if (b >= 0xC2 && b <= 0xDF) { c = b & 0x1F; size = 2; }
            else if (b >= 0xE0 && b <= 0xEF) { c = b & 0x0F; size = 3; }
            else if (b >= 0xF0 && b <= 0xF4) { c = b & 0x07; size = 4; }
            else return null;
            if (size > end - i) return null;
            for (var k = 1; k < size; k++)
            {
                var next = data[i + k];
                if ((next & 0xC0) != 0x80) return null;
                c = (c << 6) | (next & 0x3F);
            }

            if (size == 3 && c < 0x800 || size == 4 && (c < 0x10000 || c > 0x10FFFF)) return null;
            var isHigh = c >= 0xD800 && c <= 0xDBFF;
            var isLow = c >= 0xDC00 && c <= 0xDFFF;
            if (isLow && previousHigh) return null;
            if (c < 0x10000)
            {
                chars[count++] = (char)c;
            }
            else
            {
                var pair = char.ConvertFromUtf32(c);
                chars[count++] = pair[0];
                chars[count++] = pair[1];
            }

            previousHigh = isHigh;
            i += size;
        }

        return new string(chars, 0, count);
    }
}
