using System;
using System.Collections.Generic;
using System.Text;

namespace ProbablyStolen.DigitalStorage.Api.Stacks;

/// <summary>Validates a detached version 1 identity envelope without interpreting native objects.</summary>
internal sealed class CanonicalIdentityValidator
{
    private const int MaxBytes = 1048576;
    private const int MaxStringBytes = 65536;
    private const int MaxNodes = 8192;
    private const int MaxItems = 8192;
    private const int MaxEdges = 65536;
    private const int MaxRecords = 65536;
    private const int MaxNodeDepth = 16;
    private const int MaxItemDepth = 8;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] _data;
    private readonly List<PathStep> _path = new();
    private readonly HashSet<string> _featureIds = new(StringComparer.Ordinal);
    private int _offset;
    private int _nodes;
    private int _items;
    private int _edges;
    private int _records;
    private CanonicalItemSummary.Builder? _summary;

    private CanonicalIdentityValidator(byte[] data) => _data = data;

    /// <summary>Validates an envelope and returns what its root item records, or null when it is invalid.</summary>
    internal static CanonicalItemSummary? ReadSummary(byte[] data, string expectedIdentifier)
    {
        if (data == null || data.Length > MaxBytes || string.IsNullOrWhiteSpace(expectedIdentifier))
        {
            return null;
        }

        try
        {
            var reader = new CanonicalIdentityValidator(data) { _summary = new CanonicalItemSummary.Builder() };
            return reader.ReadEnvelope(expectedIdentifier) ? reader._summary.Build() : null;
        }
        catch (InvalidIdentityException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    internal static bool IsValid(byte[] data, string expectedIdentifier)
    {
        if (data == null || data.Length > MaxBytes || string.IsNullOrWhiteSpace(expectedIdentifier))
        {
            return false;
        }

        try
        {
            var reader = new CanonicalIdentityValidator(data);
            return reader.ReadEnvelope(expectedIdentifier);
        }
        catch (InvalidIdentityException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private bool ReadEnvelope(string expectedIdentifier)
    {
        Require(ReadByte() == (byte)'D' && ReadByte() == (byte)'S' && ReadByte() == (byte)'I' && ReadByte() == (byte)'D');
        Require(ReadUInt32() == 1);
        ReadItem(false, 0, 0, expectedIdentifier);
        return _offset == _data.Length;
    }

    private void ReadItem(bool child, int itemDepth, int nodeDepth, string? expectedIdentifier = null)
    {
        Require(itemDepth <= MaxItemDepth && ++_items <= MaxItems);
        Require(ReadByte() == (child ? 11 : 10));
        if (child)
        {
            Require(ReadInt32() > 0);
            ReadShape();
            ReadShape();
        }

        Require(ReadByte() == 1);
        // Only the root item describes the identity; children belong to its content window.
        var capture = !child && itemDepth == 0 ? _summary : null;
        var identifier = ReadString();
        Require(!string.IsNullOrWhiteSpace(identifier));
        if (expectedIdentifier != null)
        {
            Require(string.Equals(identifier, expectedIdentifier, StringComparison.Ordinal));
        }

        if (capture != null) capture.Identifier = identifier!;

        ReadString();
        ReadString();
        ReadString();
        Skip(32);
        var itemTypes = ReadItemTypeCount();
        for (var i = 0; i < itemTypes; i++)
        {
            var type = ReadString();
            Require(type != null);
            capture?.ItemTypes.Add(type!);
        }

        var atlas = ReadString();
        var sprite = ReadString();
        if (capture != null)
        {
            capture.SpriteAtlasPath = atlas;
            capture.SpritePath = sprite;
        }
        var tagCount = ReadCount();
        string? previous = null;
        for (var i = 0; i < tagCount; i++)
        {
            AddRecord();
            var key = ReadString();
            Require(key != null && (previous == null || StringComparer.Ordinal.Compare(previous, key) < 0));
            previous = key;
            ReadString();
            ReadString();
            var enabled = ReadBool();
            var valueString = ReadString();
            var intValue = ReadInt32();
            var floatBits = ReadUInt32();
            Require((floatBits & 0x7f800000U) != 0x7f800000U);
            var longValue = ReadInt64Bits();
            var doubleBits = ReadInt64Bits();
            Require((doubleBits & 0x7ff0000000000000UL) != 0x7ff0000000000000UL);
            var boolValue = ReadBool();
            Require(enabled || !string.IsNullOrEmpty(valueString) || intValue != 0 ||
                (floatBits & 0x7fffffffU) != 0 || longValue != 0 ||
                (doubleBits & 0x7fffffffffffffffUL) != 0 || boolValue);
            capture?.Tags.Add(key!, new CanonicalItemSummary.Tag(enabled, valueString, intValue));
        }

        var featureCount = ReadCount();
        for (var i = 0; i < featureCount; i++)
        {
            ReadFeature(capture);
        }

        ReadItemWindow(1, itemDepth, nodeDepth);
        ReadItemWindow(2, itemDepth, nodeDepth);
    }

    private void ReadFeature(CanonicalItemSummary.Builder? capture)
    {
        AddRecord();
        var featureType = ReadInt32();
        Require(featureType >= 0 && featureType <= 5);
        var stage = ReadInt32();
        Require(stage >= 0 && stage <= 2);
        var count = ReadCount();
        Require(count == _path.Count);
        for (var i = 0; i < count; i++)
        {
            var kind = ReadByte();
            Require(kind == _path[i].Kind);
            if (kind == 3 || kind == 4)
            {
                Require(ReadUInt32() == _path[i].Index);
            }
        }

        ReadString();
        ReadString();
        ReadString();
        ReadInt32();
        ReadBool();
        var exposed = ReadBool();
        ReadBool();
        ReadBool();
        ReadOptionalCondition();
        ReadOptionalCondition();
        ReadBool();
        ReadBool();
        ReadInt32();
        ReadBool();
        ReadBool();
        ReadInt32();
        var disabled = ReadBool();
        var id = ReadString();
        Require(!string.IsNullOrWhiteSpace(id));
        Require(_featureIds.Add(id!));
        var category = ReadString();
        Require(category != null && !string.Equals(category, "contrabandMarkUp", StringComparison.Ordinal));
        ReadBool();
        capture?.Features.Add(new CanonicalItemSummary.Feature(id!, exposed, disabled));
    }

    private void ReadOptionalCondition()
    {
        if (!ReadBool()) return;
        AddRecord();
        ReadString();
        ReadString();
        ReadInt32();
        ReadInt32();
        ReadBool();
        ReadString();
        ReadBool();
        ReadInt32();
        ReadString();
        ReadInt32();
    }

    private void ReadItemWindow(byte kind, int itemDepth, int nodeDepth)
    {
        if (!ReadBool()) return;
        AddEdge();
        _path.Add(new PathStep(kind));
        ReadNode(itemDepth, nodeDepth + 1);
        _path.RemoveAt(_path.Count - 1);
    }

    private void ReadNode(int itemDepth, int nodeDepth)
    {
        Require(nodeDepth <= MaxNodeDepth && ++_nodes <= MaxNodes);
        var kind = ReadByte();
        switch (kind)
        {
            case 1:
                ReadNodes(itemDepth, nodeDepth);
                break;
            case 2:
                ReadShape();
                ReadItems(itemDepth, nodeDepth);
                break;
            case 3:
                RequireDimension(ReadInt32());
                RequireDimension(ReadInt32());
                ReadBool();
                ReadItems(itemDepth, nodeDepth);
                break;
            case 4:
                var orientation = ReadInt32();
                Require(orientation >= 0 && orientation <= 3);
                if (ReadBool())
                {
                    AddEdge();
                    _path.Add(new PathStep(5));
                    ReadItem(true, itemDepth + 1, nodeDepth);
                    _path.RemoveAt(_path.Count - 1);
                }

                ReadNodes(itemDepth, nodeDepth);
                break;
            default:
                throw new InvalidIdentityException();
        }
    }

    private void ReadNodes(int itemDepth, int nodeDepth)
    {
        var count = ReadCount();
        for (var i = 0; i < count; i++)
        {
            AddEdge();
            _path.Add(new PathStep(3, (uint)i));
            ReadNode(itemDepth, nodeDepth + 1);
            _path.RemoveAt(_path.Count - 1);
        }
    }

    private void ReadItems(int itemDepth, int nodeDepth)
    {
        var count = ReadCount();
        for (var i = 0; i < count; i++)
        {
            AddEdge();
            _path.Add(new PathStep(4, (uint)i));
            ReadItem(true, itemDepth + 1, nodeDepth);
            _path.RemoveAt(_path.Count - 1);
        }
    }

    private void ReadShape()
    {
        var x = ReadInt32();
        var y = ReadInt32();
        var width = ReadInt32();
        var height = ReadInt32();
        RequireDimension(width);
        RequireDimension(height);
        var area = (long)width * height;
        Require(area <= MaxBytes);
        ReadBool();
        var orientation = ReadInt32();
        Require(orientation >= 0 && orientation <= 3);
        var globalWidth = orientation % 2 == 0 ? width : height;
        var globalHeight = orientation % 2 == 0 ? height : width;
        Require((long)x + globalWidth - 1 <= int.MaxValue && (long)y + globalHeight - 1 <= int.MaxValue);
        Require(ReadByte() <= 1);
        Require(ReadUInt32() == area);
        Require(area <= _data.Length - _offset);
        for (var i = 0; i < area; i++) Require(ReadByte() <= 1);
    }

    private static void RequireDimension(int value) => Require(value >= 1 && value <= 4096);
    private void AddEdge() => Require(++_edges <= MaxEdges);
    private void AddRecord() => Require(++_records <= MaxRecords);

    private int ReadItemTypeCount()
    {
        var count = ReadUInt32();
        // Each string that is not null needs a marker and four bytes for its length.
        Require(count <= (uint)(_data.Length - _offset) / 5);
        return (int)count;
    }

    private int ReadCount()
    {
        var count = ReadUInt32();
        Require(count <= MaxEdges && count <= _data.Length - _offset);
        return (int)count;
    }

    private string? ReadString()
    {
        var present = ReadByte();
        Require(present <= 1);
        if (present == 0) return null;
        var length = ReadUInt32();
        Require(length <= MaxStringBytes && length <= _data.Length - _offset);
        var result = Utf8.GetString(_data, _offset, (int)length);
        _offset += (int)length;
        return result;
    }

    private bool ReadBool()
    {
        var value = ReadByte();
        Require(value <= 1);
        return value == 1;
    }

    private byte ReadByte()
    {
        Require(_offset < _data.Length);
        return _data[_offset++];
    }

    private uint ReadUInt32()
    {
        Require(_data.Length - _offset >= 4);
        var value = ((uint)_data[_offset] << 24) | ((uint)_data[_offset + 1] << 16) |
                    ((uint)_data[_offset + 2] << 8) | _data[_offset + 3];
        _offset += 4;
        return value;
    }

    private int ReadInt32() => unchecked((int)ReadUInt32());
    private ulong ReadInt64Bits() => ((ulong)ReadUInt32() << 32) | ReadUInt32();
    private void Skip(int count) { Require(count <= _data.Length - _offset); _offset += count; }
    private static void Require(bool condition) { if (!condition) throw new InvalidIdentityException(); }

    private readonly struct PathStep
    {
        internal PathStep(byte kind, uint index = 0) { Kind = kind; Index = index; }
        internal byte Kind { get; }
        internal uint Index { get; }
    }

    private sealed class InvalidIdentityException : Exception { }
}
