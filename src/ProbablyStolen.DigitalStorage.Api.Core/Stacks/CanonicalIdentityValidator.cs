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
    private bool _decode;

    private CanonicalIdentityValidator(byte[] data) => _data = data;

    /// <summary>Validates an envelope and returns what its root item records, or null when it is invalid.</summary>
    internal static CanonicalItemSummary? ReadSummary(byte[] data, string expectedIdentifier)
    {
        if (!Acceptable(data, expectedIdentifier)) return null;
        try
        {
            var reader = new CanonicalIdentityValidator(data) { _summary = new CanonicalItemSummary.Builder() };
            return reader.ReadEnvelope(expectedIdentifier) != null ? reader._summary.Build() : null;
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

    /// <summary>
    /// Validates an envelope and rebuilds the detached input it was encoded from, or returns null when it is invalid.
    /// Encoding the result again yields the same bytes.
    /// </summary>
    internal static CanonicalIdentityInput? Decode(byte[] data, string expectedIdentifier)
    {
        if (!Acceptable(data, expectedIdentifier)) return null;
        try
        {
            var reader = new CanonicalIdentityValidator(data) { _decode = true };
            return reader.ReadEnvelope(expectedIdentifier)?.Item;
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
        if (!Acceptable(data, expectedIdentifier)) return false;
        try
        {
            return new CanonicalIdentityValidator(data).ReadEnvelope(expectedIdentifier) != null;
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

    private static bool Acceptable(byte[] data, string expectedIdentifier) =>
        data != null && data.Length <= MaxBytes && !string.IsNullOrWhiteSpace(expectedIdentifier);

    /// <summary>Returns the root item, or null when bytes remain after it.</summary>
    private DecodedItem? ReadEnvelope(string expectedIdentifier)
    {
        Require(ReadByte() == (byte)'D' && ReadByte() == (byte)'S' && ReadByte() == (byte)'I' && ReadByte() == (byte)'D');
        Require(ReadUInt32() == 1);
        var root = ReadItem(false, 0, 0, expectedIdentifier);
        return _offset == _data.Length ? root : null;
    }

    private DecodedItem ReadItem(bool child, int itemDepth, int nodeDepth, string? expectedIdentifier = null)
    {
        Require(itemDepth <= MaxItemDepth && ++_items <= MaxItems);
        Require(ReadByte() == (child ? 11 : 10));
        var unitCount = 0;
        CanonicalIdentityShape? originalShape = null;
        CanonicalIdentityShape? modifiedShape = null;
        if (child)
        {
            unitCount = ReadInt32();
            Require(unitCount > 0);
            originalShape = ReadShape();
            modifiedShape = ReadShape();
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

        var name = ReadString();
        var shortDescription = ReadString();
        var flavorText = ReadString();
        var unitBaseValue = ReadInt64();
        var unitValue = ReadInt64();
        var lateUnitValue = ReadInt64();
        var backupUnitValue = ReadInt64();
        var itemTypeCount = ReadItemTypeCount();
        var itemTypes = _decode ? new List<string>(itemTypeCount) : null;
        for (var i = 0; i < itemTypeCount; i++)
        {
            var type = ReadString();
            Require(type != null);
            capture?.ItemTypes.Add(type!);
            itemTypes?.Add(type!);
        }

        var atlas = ReadString();
        var sprite = ReadString();
        if (capture != null)
        {
            capture.SpriteAtlasPath = atlas;
            capture.SpritePath = sprite;
        }

        var tagCount = ReadCount();
        var tags = _decode ? new List<CanonicalIdentityTag>(tagCount) : null;
        string? previous = null;
        for (var i = 0; i < tagCount; i++)
        {
            AddRecord();
            var key = ReadString();
            Require(key != null && (previous == null || StringComparer.Ordinal.Compare(previous, key) < 0));
            previous = key;
            var tagIdentifier = ReadString();
            var tagIdentifierName = ReadString();
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
            tags?.Add(new CanonicalIdentityTag(key!, tagIdentifier, tagIdentifierName, enabled, valueString, intValue,
                floatBits, unchecked((long)longValue), doubleBits, boolValue));
        }

        var featureCount = ReadCount();
        var features = _decode ? new List<CanonicalIdentityFeature>(featureCount) : null;
        for (var i = 0; i < featureCount; i++)
        {
            var feature = ReadFeature(capture);
            if (feature != null) features!.Add(feature);
        }

        var content = ReadItemWindow(1, itemDepth, nodeDepth);
        var examine = ReadItemWindow(2, itemDepth, nodeDepth);
        if (!_decode) return default;
        var item = new CanonicalIdentityInput(identifier!, itemTypes!, tags!, features!, name, shortDescription,
            flavorText, unitBaseValue, unitValue, lateUnitValue, backupUnitValue, atlas, sprite, content, examine);
        return new DecodedItem(item,
            child ? new CanonicalIdentityChild(item, unitCount, originalShape!, modifiedShape!) : null);
    }

    private CanonicalIdentityFeature? ReadFeature(CanonicalItemSummary.Builder? capture)
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

        var publicDisplay = ReadString();
        var actualDisplay = ReadString();
        var removedByTool = ReadString();
        var valueModifier = ReadInt32();
        var initiallyShown = ReadBool();
        var exposed = ReadBool();
        var exposing = ReadBool();
        var discovered = ReadBool();
        var fakeCondition = ReadOptionalCondition();
        var realCondition = ReadOptionalCondition();
        var useCondition = ReadBool();
        var usePreExposeValue = ReadBool();
        var preExposeValueModifier = ReadInt32();
        var exposable = ReadBool();
        var publicHidden = ReadBool();
        var customIntValue1 = ReadInt32();
        var disabled = ReadBool();
        var id = ReadString();
        Require(!string.IsNullOrWhiteSpace(id));
        Require(_featureIds.Add(id!));
        var category = ReadString();
        Require(category != null && !string.Equals(category, "contrabandMarkUp", StringComparison.Ordinal));
        var featureMatch = ReadBool();
        capture?.Features.Add(new CanonicalItemSummary.Feature(id!, exposed, disabled));
        return _decode
            ? new CanonicalIdentityFeature(id!, category!, featureType, stage, publicDisplay, actualDisplay,
                removedByTool, valueModifier, initiallyShown, exposed, exposing, discovered, fakeCondition,
                realCondition, useCondition, usePreExposeValue, preExposeValueModifier, exposable, publicHidden,
                customIntValue1, disabled, featureMatch)
            : null;
    }

    private CanonicalIdentityCondition? ReadOptionalCondition()
    {
        if (!ReadBool()) return null;
        AddRecord();
        var display = ReadString();
        var identifier = ReadString();
        var modValue = ReadInt32();
        var modValueDisplay = ReadInt32();
        var transformative = ReadBool();
        var category = ReadString();
        var hiddenAsPublic = ReadBool();
        var customValue = ReadInt32();
        var actionId = ReadString();
        var compareValue = ReadInt32();
        return _decode
            ? new CanonicalIdentityCondition(display, identifier, modValue, modValueDisplay, transformative, category,
                hiddenAsPublic, customValue, actionId, compareValue)
            : null;
    }

    private CanonicalIdentityNode? ReadItemWindow(byte kind, int itemDepth, int nodeDepth)
    {
        if (!ReadBool()) return null;
        AddEdge();
        _path.Add(new PathStep(kind));
        var node = ReadNode(itemDepth, nodeDepth + 1);
        _path.RemoveAt(_path.Count - 1);
        return node;
    }

    private CanonicalIdentityNode? ReadNode(int itemDepth, int nodeDepth)
    {
        Require(nodeDepth <= MaxNodeDepth && ++_nodes <= MaxNodes);
        var kind = ReadByte();
        switch (kind)
        {
            case 1:
                {
                    var nodes = ReadNodes(itemDepth, nodeDepth);
                    return _decode ? CanonicalIdentityNode.Window(nodes!) : null;
                }
            case 2:
                {
                    var shape = ReadShape();
                    var items = ReadItems(itemDepth, nodeDepth);
                    return _decode ? CanonicalIdentityNode.Grid(shape!, items!) : null;
                }
            case 3:
                {
                    var width = ReadInt32();
                    RequireDimension(width);
                    var height = ReadInt32();
                    RequireDimension(height);
                    var alwaysStack = ReadBool();
                    var items = ReadItems(itemDepth, nodeDepth);
                    return _decode ? CanonicalIdentityNode.Scrollable(width, height, alwaysStack, items!) : null;
                }
            case 4:
                {
                    var orientation = ReadInt32();
                    Require(orientation >= 0 && orientation <= 3);
                    CanonicalIdentityChild? slotItem = null;
                    if (ReadBool())
                    {
                        AddEdge();
                        _path.Add(new PathStep(5));
                        slotItem = ReadItem(true, itemDepth + 1, nodeDepth).Child;
                        _path.RemoveAt(_path.Count - 1);
                    }

                    var nodes = ReadNodes(itemDepth, nodeDepth);
                    return _decode ? CanonicalIdentityNode.Slot(orientation, slotItem, nodes!) : null;
                }
            default:
                throw new InvalidIdentityException();
        }
    }

    private List<CanonicalIdentityNode>? ReadNodes(int itemDepth, int nodeDepth)
    {
        var count = ReadCount();
        var nodes = _decode ? new List<CanonicalIdentityNode>(count) : null;
        for (var i = 0; i < count; i++)
        {
            AddEdge();
            _path.Add(new PathStep(3, (uint)i));
            var node = ReadNode(itemDepth, nodeDepth + 1);
            if (node != null) nodes!.Add(node);
            _path.RemoveAt(_path.Count - 1);
        }

        return nodes;
    }

    private List<CanonicalIdentityChild>? ReadItems(int itemDepth, int nodeDepth)
    {
        var count = ReadCount();
        var items = _decode ? new List<CanonicalIdentityChild>(count) : null;
        for (var i = 0; i < count; i++)
        {
            AddEdge();
            _path.Add(new PathStep(4, (uint)i));
            var child = ReadItem(true, itemDepth + 1, nodeDepth).Child;
            if (child != null) items!.Add(child);
            _path.RemoveAt(_path.Count - 1);
        }

        return items;
    }

    private CanonicalIdentityShape? ReadShape()
    {
        var x = ReadInt32();
        var y = ReadInt32();
        var width = ReadInt32();
        var height = ReadInt32();
        RequireDimension(width);
        RequireDimension(height);
        var area = (long)width * height;
        Require(area <= MaxBytes);
        var flipped = ReadBool();
        var orientation = ReadInt32();
        Require(orientation >= 0 && orientation <= 3);
        var globalWidth = orientation % 2 == 0 ? width : height;
        var globalHeight = orientation % 2 == 0 ? height : width;
        Require((long)x + globalWidth - 1 <= int.MaxValue && (long)y + globalHeight - 1 <= int.MaxValue);
        var outside = ReadByte();
        Require(outside <= 1);
        Require(ReadUInt32() == area);
        Require(area <= _data.Length - _offset);
        var mask = _decode ? new byte[area] : null;
        for (var i = 0; i < area; i++)
        {
            var bit = ReadByte();
            Require(bit <= 1);
            if (mask != null) mask[i] = bit;
        }

        return mask == null ? null : new CanonicalIdentityShape(x, y, width, height, flipped, orientation, outside, mask);
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
    private long ReadInt64() => unchecked((long)ReadInt64Bits());
    private static void Require(bool condition) { if (!condition) throw new InvalidIdentityException(); }

    private readonly struct PathStep
    {
        internal PathStep(byte kind, uint index = 0) { Kind = kind; Index = index; }
        internal byte Kind { get; }
        internal uint Index { get; }
    }

    private readonly struct DecodedItem
    {
        internal DecodedItem(CanonicalIdentityInput item, CanonicalIdentityChild? child) { Item = item; Child = child; }
        internal CanonicalIdentityInput? Item { get; }
        internal CanonicalIdentityChild? Child { get; }
    }

    private sealed class InvalidIdentityException : Exception { }
}
