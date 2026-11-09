using System;
using System.IO;
using System.Text;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>Encodes one canonical filter identity as single-line Base64 text for the partition tag.</summary>
internal static class CanonicalFilterKeyCodec
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static string Encode(ItemKey key) => Encode(key, int.MaxValue);

    public static string Encode(ItemKey key, int maxEncodedLength)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (!key.IsCanonical) throw new ArgumentException("A filter key must use canonical identity.", nameof(key));

        var identifierLength = Utf8.GetByteCount(key.Identifier);
        var state = key.CopyCanonicalState();
        var encodedLength = 4L * ((8L + identifierLength + state.Length + 2) / 3);
        if (encodedLength > maxEncodedLength) throw new ArgumentException("The filter key exceeds the metadata budget.", nameof(key));
        var identifier = Utf8.GetBytes(key.Identifier);
        using var stream = new MemoryStream();
        stream.WriteByte((byte)'C');
        stream.WriteByte((byte)'F');
        stream.WriteByte((byte)'K');
        stream.WriteByte(1);
        WriteUInt32(stream, checked((uint)identifier.Length));
        stream.Write(identifier, 0, identifier.Length);
        stream.Write(state, 0, state.Length);
        return Convert.ToBase64String(stream.ToArray());
    }

    public static bool TryDecode(string encoded, out ItemKey? key)
    {
        try
        {
            key = Decode(encoded);
            return true;
        }
        catch (ArgumentException)
        {
            key = null;
            return false;
        }
    }

    public static ItemKey Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) throw new ArgumentException("A filter key is required.", nameof(encoded));
        byte[] data;
        try
        {
            data = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The filter key is not canonical text.", nameof(encoded), exception);
        }

        if (!string.Equals(Convert.ToBase64String(data), encoded, StringComparison.Ordinal))
        {
            throw new ArgumentException("The filter key is not canonical text.", nameof(encoded));
        }

        if (data.Length < 8 || data[0] != 'C' || data[1] != 'F' || data[2] != 'K' || data[3] != 1)
        {
            throw new ArgumentException("The filter key format is unsupported.", nameof(encoded));
        }

        var identifierLength = ReadUInt32(data, 4);
        if (identifierLength > int.MaxValue || identifierLength > data.Length - 8)
        {
            throw new ArgumentException("The filter key identifier is invalid.", nameof(encoded));
        }

        var identifierBytes = new byte[(int)identifierLength];
        Buffer.BlockCopy(data, 8, identifierBytes, 0, identifierBytes.Length);
        var identifier = Utf8.GetString(identifierBytes);
        var stateOffset = checked(8 + identifierBytes.Length);
        var state = new byte[data.Length - stateOffset];
        Buffer.BlockCopy(data, stateOffset, state, 0, state.Length);
        return ItemKey.FromCanonical(identifier, state);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];
}
