using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Platform.Identity;

/// <summary>Outcome of a native identity capture.</summary>
internal enum NativeItemIdentityReadStatus
{
    Success = 0,
    Unsupported = 1,
    Stale = 2
}

/// <summary>Reason a native identity was not emitted.</summary>
internal enum NativeItemIdentityFailure
{
    None = 0,
    NullRoot = 1,
    UnsupportedType = 2,
    UnsupportedGraph = 3,
    UnsupportedMember = 4,
    InvalidNativeState = 5,
    OwnershipMismatch = 6,
    SharedReference = 7,
    Cycle = 8,
    ReadException = 9,
    EncodingRejected = 10
}

/// <summary>Result of a native identity capture without mutations.</summary>
internal sealed class NativeItemIdentityReadResult
{
    private NativeItemIdentityReadResult(NativeItemIdentityReadStatus status, NativeItemIdentityFailure failure,
        ItemKey? identity)
    {
        Status = status;
        Failure = failure;
        Identity = identity;
    }

    internal NativeItemIdentityReadStatus Status { get; }
    internal NativeItemIdentityFailure Failure { get; }
    internal ItemKey? Identity { get; }

    internal static NativeItemIdentityReadResult Success(ItemKey identity) =>
        new(NativeItemIdentityReadStatus.Success, NativeItemIdentityFailure.None, identity);

    internal static NativeItemIdentityReadResult Unsupported(NativeItemIdentityFailure failure) =>
        new(NativeItemIdentityReadStatus.Unsupported, failure, null);

    internal static NativeItemIdentityReadResult Stale(NativeItemIdentityFailure failure) =>
        new(NativeItemIdentityReadStatus.Stale, failure, null);
}

/// <summary>
/// Reads the supported native item structure into the detached identity contract.
/// This adapter never creates, changes, or invokes behavior on a native item.
/// </summary>
internal sealed class NativeItemIdentityReader
{
    internal const int MaxNodes = 8192;
    internal const int MaxItems = 8192;
    internal const int MaxEdges = 65536;
    internal const int MaxRecords = 65536;
    internal const int MaxNodeDepth = 16;
    internal const int MaxItemDepth = 8;
    private readonly HashSet<object> _seen = new(ReferenceComparer.Instance);
    private readonly HashSet<object> _active = new(ReferenceComparer.Instance);
    private readonly Dictionary<int, INativeIdentityItemSource> _owners = new();
    private readonly List<IdentityPathStep> _path = new();
    private readonly NativeIdentityCaptureBudget _budget = new();
    private int _nodes;
    private int _items;
    private int _edges;
    private int _records;

    private NativeItemIdentityReader()
    {
    }

    /// <summary>
    /// Reads the type of a live native item. The root's position, orientation, flip and unique ID are not part of
    /// the identity, so the same item stored in another orientation aggregates with it. Call on the main thread
    /// outside native inventory callbacks.
    /// </summary>
    internal static NativeItemIdentityReadResult Read(GameItem root)
    {
        if (root == null)
        {
            return NativeItemIdentityReadResult.Unsupported(NativeItemIdentityFailure.NullRoot);
        }

        try
        {
            return Read(new NativeIdentityAdapter().WrapItem(root));
        }
        catch (UnsupportedIdentityException exception)
        {
            return NativeItemIdentityReadResult.Unsupported(exception.Failure);
        }
        catch (Exception)
        {
            return NativeItemIdentityReadResult.Stale(NativeItemIdentityFailure.ReadException);
        }
    }

    /// <summary>Captures a source double without loading or executing target game code.</summary>
    internal static NativeItemIdentityReadResult Read(INativeIdentityItemSource root)
    {
        if (root == null)
        {
            return NativeItemIdentityReadResult.Unsupported(NativeItemIdentityFailure.NullRoot);
        }

        try
        {
            var reader = new NativeItemIdentityReader();
            var input = reader.ReadItem(root, null, 0, 0);
            return NativeItemIdentityReadResult.Success(CanonicalIdentityEncoder.Encode(input));
        }
        catch (UnsupportedIdentityException exception)
        {
            return NativeItemIdentityReadResult.Unsupported(exception.Failure);
        }
        catch (StaleIdentityException exception)
        {
            return NativeItemIdentityReadResult.Stale(exception.Failure);
        }
        catch (ArgumentException)
        {
            return NativeItemIdentityReadResult.Unsupported(NativeItemIdentityFailure.EncodingRejected);
        }
        catch (Exception)
        {
            return NativeItemIdentityReadResult.Stale(NativeItemIdentityFailure.ReadException);
        }
    }

    internal static void ValidateNativeString(string? value) =>
        NativeIdentityCaptureBudget.ValidateString(value);

    private CanonicalIdentityInput ReadItem(INativeIdentityItemSource item, INativeIdentityInventorySource? containingInventory,
        int itemDepth, int nodeDepth)
    {
        if (itemDepth < 0 || itemDepth > MaxItemDepth || ++_items > MaxItems)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }

        RequireSupported(item.Kind, NativeIdentityItemKind.GameItemElement,
            NativeItemIdentityFailure.UnsupportedType);
        using var visit = Enter(item);
        RegisterOwner(item);

        var parent = item.ParentInventory;
        if (containingInventory != null)
        {
            RequireReference(parent, containingInventory, NativeItemIdentityFailure.OwnershipMismatch);
            RequireMembership(containingInventory.Items, item);
        }
        else if (parent != null)
        {
            RequireMembership(parent.Items, item);
        }

        var itemTypesSource = item.ItemTypes;
        var tagsSource = item.Tags;
        var featuresSource = item.Features;
        if (itemTypesSource == null || tagsSource == null || featuresSource == null)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        _budget.RequireStringCount(itemTypesSource.Count);
        RequireCount(tagsSource.Count, MaxRecords);
        RequireCount(featuresSource.Count, MaxRecords);
        ReserveRecordCount(checked(tagsSource.Count + featuresSource.Count));
        _budget.ReserveItem(item);
        var itemTypes = CopyStrings(itemTypesSource);
        var tags = ReadTags(tagsSource);
        var features = ReadFeatures(featuresSource, item);
        var content = ReadWindow(item.ContentWindow, item, 1, itemDepth, nodeDepth);
        var examine = ReadWindow(item.ExamineWindow, item, 2, itemDepth, nodeDepth);

        return new CanonicalIdentityInput(item.Identifier, itemTypes, tags, features,
            item.Name, item.ShortDescription, item.FlavorText, item.UnitBaseValue, item.UnitValue,
            item.LateUnitValue, item.BackupUnitValue, item.SpriteAtlasPath, item.SpritePath, content, examine);
    }

    private IReadOnlyList<CanonicalIdentityTag> ReadTags(IReadOnlyList<INativeIdentityTagSource> source)
    {
        if (source == null)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        var count = source.Count;
        RequireCount(count, MaxRecords);
        for (var index = 0; index < count; index++)
        {
            var tag = source[index];
            if (tag == null)
            {
                throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
            }

            _budget.ReserveTag(tag);
        }

        var result = new List<CanonicalIdentityTag>(count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var tag = source[index];
            if (tag == null)
            {
                throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
            }

            using var visit = Enter(tag);
            if (tag.Key == null || !keys.Add(tag.Key))
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
            }

            result.Add(new CanonicalIdentityTag(tag.Key, tag.Identifier, tag.IdentifierName, tag.Enabled,
                tag.ValueString, tag.ValueInt, tag.ValueFloatBits, tag.ValueLong, tag.ValueDoubleBits, tag.ValueBool));
        }

        if (source.Count != count)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        return new ReadOnlyCollection<CanonicalIdentityTag>(result);
    }

    private IReadOnlyList<CanonicalIdentityFeature> ReadFeatures(IReadOnlyList<INativeIdentityFeatureSource> source,
        INativeIdentityItemSource owner)
    {
        if (source == null)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        var count = source.Count;
        RequireCount(count, MaxRecords);
        for (var index = 0; index < count; index++)
        {
            var feature = source[index];
            if (feature == null)
            {
                throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
            }

            _budget.ReserveFeature(feature, _path);
            if (feature.PrivateFields != null)
            {
                if (feature.FakeCondition != null) ReserveRecordCount(1);
                if (feature.RealCondition != null) ReserveRecordCount(1);
            }
        }

        var result = new List<CanonicalIdentityFeature>(count);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var feature = source[index];
            if (feature == null)
            {
                throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
            }

            using var visit = Enter(feature);
            if (feature.Identifier == null || feature.Category == null || !identifiers.Add(feature.Identifier))
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
            }

            if (feature.OwnerUniqueId <= 0 || owner.UniqueId <= 0 || feature.OwnerUniqueId != owner.UniqueId)
            {
                throw new StaleIdentityException(NativeItemIdentityFailure.OwnershipMismatch);
            }

            var fake = ReadCondition(feature.FakeCondition);
            var real = ReadCondition(feature.RealCondition);
            var privateFields = feature.PrivateFields;
            if (privateFields == null || privateFields.PublicDisplay == null && feature.PublicDisplayUnavailable)
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.UnsupportedMember);
            }

            result.Add(new CanonicalIdentityFeature(feature.Identifier, feature.Category, feature.FeatureType,
                feature.ValueStage, privateFields.PublicDisplay, privateFields.ActualDisplay,
                privateFields.RemovedByTool, privateFields.ValueModifier, privateFields.InitiallyShown,
                privateFields.IsFeatureExposed, feature.IsExposing, privateFields.IsFeatureDiscovered, fake, real,
                feature.UseCondition, privateFields.UsePreExposeValue, privateFields.PreExposeValueModifier,
                privateFields.IsExposable, feature.IsPublicHidden, feature.CustomIntValue1, feature.IsDisabled,
                feature.IsFeatureMatch));
        }

        if (source.Count != count)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        return new ReadOnlyCollection<CanonicalIdentityFeature>(result);
    }

    private CanonicalIdentityCondition? ReadCondition(INativeIdentityConditionSource? condition)
    {
        if (condition == null)
        {
            return null;
        }

        using var visit = Enter(condition);
        // Conditions such as "counterfeitInjector" name an activation action (ItemConditionList); the action ID
        // is part of the identity, so items with different actions stay separate types.
        return new CanonicalIdentityCondition(condition.Display, condition.Identifier, condition.ModValue,
            condition.ModValueDisplay, condition.IsTransformative, condition.Category, condition.HiddenAsPublic,
            condition.CustomValue, condition.OnActivateActionId, condition.CompareValue);
    }

    private CanonicalIdentityNode? ReadWindow(INativeIdentityWindowSource? window, INativeIdentityItemSource owner,
        byte role, int itemDepth, int nodeDepth)
    {
        if (window == null)
        {
            return null;
        }

        var stableWindow = window;
        AddEdge();
        using var visit = Enter(stableWindow);
        RequireSingleParent(stableWindow.Parents, owner);
        var children = stableWindow.Children;
        if (children == null)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        var windowDepth = checked(nodeDepth + 1);
        if (windowDepth > MaxNodeDepth || ++_nodes > MaxNodes)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }

        RequireCount(children.Count, MaxNodes);
        RequireNodeCapacity(children.Count);
        RequireEdgeCapacity(children.Count);
        _budget.Reserve(5);
        _path.Add(new IdentityPathStep(role));
        var nodes = new List<CanonicalIdentityNode>(children.Count);
        for (var index = 0; index < children.Count; index++)
        {
            var graphNode = children[index];
            if (graphNode == null || graphNode.Kind != NativeIdentityGraphKind.Inventory || graphNode.Inventory == null)
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.UnsupportedGraph);
            }

            AddEdge();
            RequireReference(graphNode.Inventory.ParentWindow, stableWindow, NativeItemIdentityFailure.OwnershipMismatch);
            _path.Add(new IdentityPathStep(3, checked((uint)index)));
            nodes.Add(ReadNode(graphNode, itemDepth, windowDepth + 1));
            _path.RemoveAt(_path.Count - 1);
        }

        _path.RemoveAt(_path.Count - 1);
        return CanonicalIdentityNode.Window(new ReadOnlyCollection<CanonicalIdentityNode>(nodes));
    }

    private CanonicalIdentityNode ReadNode(INativeIdentityGraphSource graphNode, int itemDepth, int nodeDepth)
    {
        if (nodeDepth < 0 || nodeDepth > MaxNodeDepth || ++_nodes > MaxNodes)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }

        _budget.Reserve(1);

        using var graphVisit = Enter(graphNode);
        var inventory = graphNode.Inventory;
        if (graphNode.Kind != NativeIdentityGraphKind.Inventory || inventory == null)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.UnsupportedGraph);
        }

        using var inventoryVisit = Enter(inventory);
        switch (inventory.Kind)
        {
            case NativeIdentityInventoryKind.Grid:
                return CanonicalIdentityNode.Grid(ReadShape(inventory.Shape), ReadItems(inventory, itemDepth, nodeDepth));
            case NativeIdentityInventoryKind.Scrollable:
                RequireDimension(inventory.Width);
                RequireDimension(inventory.Height);
                _budget.Reserve(4 + 4 + 1);
                return CanonicalIdentityNode.Scrollable(inventory.Width, inventory.Height, inventory.AlwaysStack,
                    ReadItems(inventory, itemDepth, nodeDepth));
            case NativeIdentityInventoryKind.Slot:
                return ReadSlot(inventory, itemDepth, nodeDepth);
            default:
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.UnsupportedGraph);
        }
    }

    private CanonicalIdentityNode ReadSlot(INativeIdentityInventorySource inventory, int itemDepth, int nodeDepth)
    {
        if (inventory.Orientation < 0 || inventory.Orientation > 3 || inventory.SlotChildren == null)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }

        _budget.Reserve(4 + 1 + 4);

        var item = inventory.SlotItem;
        if (item != null)
        {
            RequireReference(item.ParentInventory, inventory, NativeItemIdentityFailure.OwnershipMismatch);
            RequireMembership(inventory.Items, item);
        }

        var slotChildren = inventory.SlotChildren;
        RequireCount(slotChildren.Count, 1);
        if (slotChildren.Count != (item == null ? 0 : 1))
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.UnsupportedGraph);
        }

        foreach (var child in slotChildren)
        {
            using var childVisit = Enter(child ?? throw new StaleIdentityException(
                NativeItemIdentityFailure.ReadException));
            if (child.Kind != NativeIdentityGraphKind.Item || !ReferenceEquals(child.Item, item))
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.UnsupportedGraph);
            }
        }

        CanonicalIdentityChild? slotItem = null;
        if (item != null)
        {
            AddEdge();
            _path.Add(new IdentityPathStep(5));
            slotItem = ReadChild(item, inventory, itemDepth, nodeDepth);
            _path.RemoveAt(_path.Count - 1);
        }

        var childNodes = inventory.Children;
        if (childNodes == null)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        RequireCount(childNodes.Count, MaxNodes);
        RequireNodeCapacity(childNodes.Count);
        RequireEdgeCapacity(childNodes.Count);
        var nodes = new List<CanonicalIdentityNode>(childNodes.Count);
        for (var index = 0; index < childNodes.Count; index++)
        {
            var child = childNodes[index];
            if (child == null || child.Kind != NativeIdentityGraphKind.Inventory || child.Inventory == null)
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.UnsupportedGraph);
            }

            var parentWindow = inventory.ParentWindow;
            if (parentWindow == null)
            {
                throw new StaleIdentityException(NativeItemIdentityFailure.OwnershipMismatch);
            }

            RequireReference(child.Inventory.ParentWindow, parentWindow,
                NativeItemIdentityFailure.OwnershipMismatch);
            AddEdge();
            _path.Add(new IdentityPathStep(3, checked((uint)index)));
            nodes.Add(ReadNode(child, itemDepth, nodeDepth + 1));
            _path.RemoveAt(_path.Count - 1);
        }

        return CanonicalIdentityNode.Slot(inventory.Orientation, slotItem,
            new ReadOnlyCollection<CanonicalIdentityNode>(nodes));
    }

    private IReadOnlyList<CanonicalIdentityChild> ReadItems(INativeIdentityInventorySource inventory,
        int itemDepth, int nodeDepth)
    {
        var items = inventory.Items;
        var children = inventory.Children;
        if (items == null || children == null)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        RequireCount(items.Count, MaxItems);
        RequireCount(children.Count, MaxItems);
        RequireItemCapacity(items.Count);
        RequireEdgeCapacity(items.Count);
        _budget.Reserve(4);
        if (children.Count != items.Count)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.OwnershipMismatch);
        }

        var childReferences = new HashSet<object>(ReferenceComparer.Instance);
        var result = new List<CanonicalIdentityChild>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var child = children[index];
            if (item == null || child == null || !childReferences.Add(child) ||
                child.Kind != NativeIdentityGraphKind.Item || !ReferenceEquals(child.Item, item))
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.OwnershipMismatch);
            }

            RequireReference(item.ParentInventory, inventory, NativeItemIdentityFailure.OwnershipMismatch);
            RequireMembership(items, item);
            AddEdge();
            _path.Add(new IdentityPathStep(4, checked((uint)index)));
            result.Add(ReadChild(item, inventory, itemDepth, nodeDepth));
            _path.RemoveAt(_path.Count - 1);
        }

        return new ReadOnlyCollection<CanonicalIdentityChild>(result);
    }

    private CanonicalIdentityChild ReadChild(INativeIdentityItemSource item, INativeIdentityInventorySource inventory,
        int itemDepth, int nodeDepth)
    {
        if (item.UnitCount <= 0)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }

        _budget.Reserve(4);

        var original = ReadShape(item.Shape);
        var modified = ReadShape(item.ModifiedShape);
        var value = ReadItem(item, inventory, itemDepth + 1, nodeDepth);
        return new CanonicalIdentityChild(value, item.UnitCount, original, modified);
    }

    private CanonicalIdentityShape ReadShape(INativeIdentityShapeSource? shape)
    {
        if (shape == null || shape.Kind != NativeIdentityShapeKind.GridShapeBuilder)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.UnsupportedMember);
        }

        using var visit = Enter(shape);
        RequireDimension(shape.Width);
        RequireDimension(shape.Height);
        if (shape.Orientation < 0 || shape.Orientation > 3 || shape.Outside > 1)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }

        var area = checked(shape.Width * shape.Height);
        if (area > 1048576 || shape.BackingLength != area)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }

        _budget.Reserve(26);
        _budget.Reserve(area);
        var mask = new byte[area];
        for (var y = 0; y < shape.Height; y++)
        {
            for (var x = 0; x < shape.Width; x++)
            {
                var value = shape.GetLocal(x, y);
                if (value > 1)
                {
                    throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
                }

                mask[y * shape.Width + x] = value;
            }
        }

        return new CanonicalIdentityShape(shape.MinX, shape.MinY, shape.Width, shape.Height,
            shape.Flipped, shape.Orientation, shape.Outside, mask);
    }

    private IReadOnlyList<string> CopyStrings(IReadOnlyList<string> values)
    {
        if (values == null)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        var count = values.Count;
        _budget.RequireStringCount(count);

        for (var index = 0; index < count; index++)
        {
            var value = values[index] ?? throw new UnsupportedIdentityException(
                NativeItemIdentityFailure.InvalidNativeState);
            _budget.ReserveString(value);
        }

        var result = new string[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = values[index] ?? throw new UnsupportedIdentityException(
                NativeItemIdentityFailure.InvalidNativeState);
        }

        if (values.Count != count)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        return new ReadOnlyCollection<string>(result);
    }

    private void RegisterOwner(INativeIdentityItemSource item)
    {
        if (item.UniqueId <= 0)
        {
            return;
        }

        if (_owners.TryGetValue(item.UniqueId, out var existing) && !ReferenceEquals(existing, item))
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.OwnershipMismatch);
        }

        _owners[item.UniqueId] = item;
    }

    private static void RequireMembership(IReadOnlyList<INativeIdentityItemSource> items,
        INativeIdentityItemSource item)
    {
        if (items == null)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
        }

        var count = 0;
        foreach (var candidate in items)
        {
            if (candidate == null)
            {
                throw new StaleIdentityException(NativeItemIdentityFailure.ReadException);
            }

            if (ReferenceEquals(candidate, item))
            {
                count++;
            }
        }

        if (count != 1)
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.OwnershipMismatch);
        }
    }

    private static void RequireSingleParent(IReadOnlyList<INativeIdentityItemSource> parents,
        INativeIdentityItemSource owner)
    {
        if (parents == null || parents.Count != 1 || !ReferenceEquals(parents[0], owner))
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.OwnershipMismatch);
        }
    }

    private static void RequireReference(object? actual, object expected, NativeItemIdentityFailure failure)
    {
        if (!ReferenceEquals(actual, expected))
        {
            throw new StaleIdentityException(failure);
        }
    }

    private IDisposable Enter(object value)
    {
        if (_active.Contains(value))
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.Cycle);
        }

        if (!_seen.Add(value))
        {
            throw new StaleIdentityException(NativeItemIdentityFailure.SharedReference);
        }

        _active.Add(value);
        return new ActiveReference(_active, value);
    }

    private void RequireCount(int count, int maximum)
    {
        if (count < 0 || count > maximum)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }
    }

    private void AddEdge()
    {
        if (++_edges > MaxEdges)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }
    }

    private void ReserveRecordCount(int count)
    {
        if (count < 0 || count > MaxRecords - _records)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }

        _records += count;
    }

    private void RequireNodeCapacity(int count)
    {
        if (count < 0 || count > MaxNodes - _nodes)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }
    }

    private void RequireItemCapacity(int count)
    {
        if (count < 0 || count > MaxItems - _items)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }
    }

    private void RequireEdgeCapacity(int count)
    {
        if (count < 0 || count > MaxEdges - _edges)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }
    }

    private sealed class NativeIdentityCaptureBudget
    {
        private const int MaxBytes = 1048576;
        private const int MaxStringBytes = 65536;
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private int _bytes = 8;

        internal void ReserveItem(INativeIdentityItemSource item)
        {
            Reserve(48);
            ReserveString(item.Identifier);
            ReserveString(item.Name);
            ReserveString(item.ShortDescription);
            ReserveString(item.FlavorText);
            ReserveString(item.SpriteAtlasPath);
            ReserveString(item.SpritePath);
        }

        internal void ReserveTag(INativeIdentityTagSource tag)
        {
            if (!tag.Enabled && string.IsNullOrEmpty(tag.ValueString) && tag.ValueInt == 0 &&
                (tag.ValueFloatBits & 0x7fffffffU) == 0 && tag.ValueLong == 0 &&
                (tag.ValueDoubleBits & 0x7fffffffffffffffUL) == 0 && !tag.ValueBool)
            {
                return;
            }

            Reserve(26);
            ReserveString(tag.Key);
            ReserveString(tag.Identifier);
            ReserveString(tag.IdentifierName);
            ReserveString(tag.ValueString);
        }

        internal void ReserveFeature(INativeIdentityFeatureSource feature, IReadOnlyList<IdentityPathStep> path)
        {
            var pathBytes = 4;
            foreach (var step in path)
            {
                pathBytes = checked(pathBytes + (step.Kind == 3 || step.Kind == 4 ? 5 : 1));
            }

            Reserve(30 + pathBytes);
            ReserveString(feature.Identifier);
            ReserveString(feature.Category);
            var privateFields = feature.PrivateFields;
            if (privateFields == null)
            {
                return;
            }

            ReserveString(privateFields.PublicDisplay);
            ReserveString(privateFields.ActualDisplay);
            ReserveString(privateFields.RemovedByTool);
            ReserveCondition(feature.FakeCondition);
            ReserveCondition(feature.RealCondition);
        }

        internal void ReserveCondition(INativeIdentityConditionSource? condition)
        {
            if (condition == null)
            {
                Reserve(1);
                return;
            }

            Reserve(19);
            ReserveString(condition.Display);
            ReserveString(condition.Identifier);
            ReserveString(condition.Category);
            ReserveString(condition.OnActivateActionId);
        }

        internal void ReserveString(string? value)
        {
            if (value == null)
            {
                Reserve(1);
                return;
            }

            Reserve(checked(5 + GetStringByteCount(value)));
        }

        internal static void ValidateString(string? value)
        {
            if (value == null || GetStringByteCount(value) > MaxStringBytes)
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
            }
        }

        internal void RequireStringCount(int count)
        {
            if (count < 0 || count > (MaxBytes - _bytes) / 5)
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
            }
        }

        internal void Reserve(int count)
        {
            if (count < 0 || count > MaxBytes - _bytes)
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
            }

            _bytes += count;
        }

        private static int GetStringByteCount(string value)
        {
            try
            {
                var length = Utf8.GetByteCount(value);
                if (length > MaxStringBytes)
                {
                    throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
                }

                return length;
            }
            catch (EncoderFallbackException)
            {
                throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
            }
        }
    }

    private sealed class ActiveReference : IDisposable
    {
        private readonly HashSet<object> _active;
        private readonly object _value;
        private bool _disposed;

        internal ActiveReference(HashSet<object> active, object value)
        {
            _active = active;
            _value = value;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _active.Remove(_value);
        }
    }

    private static void RequireSupported<T>(T actual, T supported, NativeItemIdentityFailure failure)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, supported))
        {
            throw new UnsupportedIdentityException(failure);
        }
    }

    private static void RequireDimension(int value)
    {
        if (value < 1 || value > 4096)
        {
            throw new UnsupportedIdentityException(NativeItemIdentityFailure.InvalidNativeState);
        }
    }

    private readonly struct IdentityPathStep
    {
        internal IdentityPathStep(byte kind, uint index = 0)
        {
            Kind = kind;
            Index = index;
        }

        internal byte Kind { get; }
        internal uint Index { get; }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    internal sealed class UnsupportedIdentityException : Exception
    {
        internal UnsupportedIdentityException(NativeItemIdentityFailure failure) => Failure = failure;
        internal NativeItemIdentityFailure Failure { get; }
    }

    internal sealed class StaleIdentityException : Exception
    {
        internal StaleIdentityException(NativeItemIdentityFailure failure) => Failure = failure;
        internal NativeItemIdentityFailure Failure { get; }
    }
}

internal enum NativeIdentityItemKind
{
    Unsupported = 0,
    GameItemElement = 1
}

internal enum NativeIdentityGraphKind
{
    Unsupported = 0,
    Item = 1,
    Inventory = 2
}

internal enum NativeIdentityInventoryKind
{
    Unsupported = 0,
    Grid = 1,
    Scrollable = 2,
    Slot = 3
}

internal enum NativeIdentityShapeKind
{
    Unsupported = 0,
    GridShapeBuilder = 1
}

internal interface INativeIdentityItemSource
{
    NativeIdentityItemKind Kind { get; }
    string Identifier { get; }
    string? Name { get; }
    string? ShortDescription { get; }
    string? FlavorText { get; }
    long UnitBaseValue { get; }
    long UnitValue { get; }
    long LateUnitValue { get; }
    long BackupUnitValue { get; }
    IReadOnlyList<string> ItemTypes { get; }
    string? SpriteAtlasPath { get; }
    string? SpritePath { get; }
    int UnitCount { get; }
    int UniqueId { get; }
    INativeIdentityInventorySource? ParentInventory { get; }
    INativeIdentityShapeSource? Shape { get; }
    INativeIdentityShapeSource? ModifiedShape { get; }
    IReadOnlyList<INativeIdentityTagSource> Tags { get; }
    IReadOnlyList<INativeIdentityFeatureSource> Features { get; }
    INativeIdentityWindowSource? ContentWindow { get; }
    INativeIdentityWindowSource? ExamineWindow { get; }
}

internal interface INativeIdentityTagSource
{
    string? Key { get; }
    string? Identifier { get; }
    string? IdentifierName { get; }
    bool Enabled { get; }
    string? ValueString { get; }
    int ValueInt { get; }
    uint ValueFloatBits { get; }
    long ValueLong { get; }
    ulong ValueDoubleBits { get; }
    bool ValueBool { get; }
}

internal interface INativeIdentityFeatureSource
{
    string? Identifier { get; }
    string? Category { get; }
    int FeatureType { get; }
    int ValueStage { get; }
    int OwnerUniqueId { get; }
    bool IsExposing { get; }
    INativeIdentityConditionSource? FakeCondition { get; }
    INativeIdentityConditionSource? RealCondition { get; }
    bool UseCondition { get; }
    bool IsPublicHidden { get; }
    int CustomIntValue1 { get; }
    bool IsDisabled { get; }
    bool IsFeatureMatch { get; }
    string? PublicDisplay { get; }
    bool PublicDisplayUnavailable { get; }
    NativeIdentityFeaturePrivateFields? PrivateFields { get; }
}

internal sealed class NativeIdentityFeaturePrivateFields
{
    internal NativeIdentityFeaturePrivateFields(string? publicDisplay, string? actualDisplay, string? removedByTool,
        int valueModifier, bool initiallyShown, bool isFeatureExposed, bool isFeatureDiscovered,
        bool usePreExposeValue, int preExposeValueModifier, bool isExposable, string? onActivateActionId)
    {
        PublicDisplay = publicDisplay;
        ActualDisplay = actualDisplay;
        RemovedByTool = removedByTool;
        ValueModifier = valueModifier;
        InitiallyShown = initiallyShown;
        IsFeatureExposed = isFeatureExposed;
        IsFeatureDiscovered = isFeatureDiscovered;
        UsePreExposeValue = usePreExposeValue;
        PreExposeValueModifier = preExposeValueModifier;
        IsExposable = isExposable;
        OnActivateActionId = onActivateActionId;
    }

    internal string? PublicDisplay { get; }
    internal string? ActualDisplay { get; }
    internal string? RemovedByTool { get; }
    internal int ValueModifier { get; }
    internal bool InitiallyShown { get; }
    internal bool IsFeatureExposed { get; }
    internal bool IsFeatureDiscovered { get; }
    internal bool UsePreExposeValue { get; }
    internal int PreExposeValueModifier { get; }
    internal bool IsExposable { get; }
    internal string? OnActivateActionId { get; }
}

internal interface INativeIdentityConditionSource
{
    string? Display { get; }
    string? Identifier { get; }
    int ModValue { get; }
    int ModValueDisplay { get; }
    bool IsTransformative { get; }
    string? Category { get; }
    bool HiddenAsPublic { get; }
    int CustomValue { get; }
    string? OnActivateActionId { get; }
    int CompareValue { get; }
}

internal interface INativeIdentityShapeSource
{
    NativeIdentityShapeKind Kind { get; }
    int MinX { get; }
    int MinY { get; }
    int Width { get; }
    int Height { get; }
    bool Flipped { get; }
    int Orientation { get; }
    byte Outside { get; }
    int BackingLength { get; }
    byte GetLocal(int x, int y);
}

internal interface INativeIdentityWindowSource
{
    IReadOnlyList<INativeIdentityItemSource> Parents { get; }
    IReadOnlyList<INativeIdentityGraphSource> Children { get; }
}

internal interface INativeIdentityGraphSource
{
    NativeIdentityGraphKind Kind { get; }
    INativeIdentityItemSource? Item { get; }
    INativeIdentityInventorySource? Inventory { get; }
}

internal interface INativeIdentityInventorySource
{
    NativeIdentityInventoryKind Kind { get; }
    INativeIdentityWindowSource? ParentWindow { get; }
    IReadOnlyList<INativeIdentityItemSource> Items { get; }
    IReadOnlyList<INativeIdentityGraphSource> Children { get; }
    INativeIdentityItemSource? SlotItem { get; }
    IReadOnlyList<INativeIdentityGraphSource> SlotChildren { get; }
    INativeIdentityShapeSource? Shape { get; }
    int Width { get; }
    int Height { get; }
    bool AlwaysStack { get; }
    int Orientation { get; }
}

/// <summary>Native object adapter. It only reads fields and getters.</summary>
internal sealed class NativeIdentityAdapter
{
    private readonly Dictionary<GameItem, NativeItemSource> _items = new(ReferenceComparer<GameItem>.Instance);
    private readonly Dictionary<GameInventory, NativeInventorySource> _inventories = new(ReferenceComparer<GameInventory>.Instance);
    private readonly Dictionary<PixelWindow, NativeWindowSource> _windows = new(ReferenceComparer<PixelWindow>.Instance);
    private readonly Dictionary<GraphNodeStorage, NativeGraphSource> _graphs = new(ReferenceComparer<GraphNodeStorage>.Instance);
    private readonly Dictionary<ItemFeature, NativeFeatureSource> _features = new(ReferenceComparer<ItemFeature>.Instance);
    private readonly Dictionary<ItemCondition, NativeConditionSource> _conditions = new(ReferenceComparer<ItemCondition>.Instance);
    private readonly Dictionary<TagState, NativeTagSource> _tags = new(ReferenceComparer<TagState>.Instance);
    private readonly Dictionary<GridShape, NativeShapeSource> _shapes = new(ReferenceComparer<GridShape>.Instance);

    internal INativeIdentityItemSource WrapItem(GameItem item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (item.GetType() != typeof(GameItemElement))
        {
            throw new NativeItemIdentityReader.UnsupportedIdentityException(
                NativeItemIdentityFailure.UnsupportedType);
        }
        if (!_items.TryGetValue(item, out var source))
        {
            source = new NativeItemSource(this, item);
            _items.Add(item, source);
        }

        return source;
    }

    internal INativeIdentityInventorySource WrapInventory(GameInventory inventory)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        var type = inventory.GetType();
        if (type != typeof(GameGridInventory) && type != typeof(GameGridScrollableInventory) &&
            type != typeof(GameSlotInventory))
        {
            throw new NativeItemIdentityReader.UnsupportedIdentityException(
                NativeItemIdentityFailure.UnsupportedGraph);
        }
        if (!_inventories.TryGetValue(inventory, out var source))
        {
            source = new NativeInventorySource(this, inventory);
            _inventories.Add(inventory, source);
        }

        return source;
    }

    internal INativeIdentityWindowSource WrapWindow(PixelWindow window)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));
        if (window.GetType() != typeof(PixelWindow))
        {
            throw new NativeItemIdentityReader.UnsupportedIdentityException(
                NativeItemIdentityFailure.UnsupportedGraph);
        }
        if (!_windows.TryGetValue(window, out var source))
        {
            source = new NativeWindowSource(this, window);
            _windows.Add(window, source);
        }

        return source;
    }

    internal INativeIdentityGraphSource WrapGraph(GraphNodeStorage graph)
    {
        if (graph == null) throw new ArgumentNullException(nameof(graph));
        if (graph is not GameItem && graph is not GameInventory)
        {
            throw new NativeItemIdentityReader.UnsupportedIdentityException(
                NativeItemIdentityFailure.UnsupportedGraph);
        }
        if (!_graphs.TryGetValue(graph, out var source))
        {
            source = new NativeGraphSource(this, graph);
            _graphs.Add(graph, source);
        }

        return source;
    }

    internal INativeIdentityFeatureSource WrapFeature(ItemFeature feature)
    {
        if (feature == null) throw new ArgumentNullException(nameof(feature));
        if (feature.GetType() != typeof(ItemFeature))
        {
            throw new NativeItemIdentityReader.UnsupportedIdentityException(
                NativeItemIdentityFailure.UnsupportedMember);
        }
        if (!_features.TryGetValue(feature, out var source))
        {
            source = new NativeFeatureSource(this, feature);
            _features.Add(feature, source);
        }

        return source;
    }

    internal INativeIdentityConditionSource WrapCondition(ItemCondition condition)
    {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        if (condition.GetType() != typeof(ItemCondition))
        {
            throw new NativeItemIdentityReader.UnsupportedIdentityException(
                NativeItemIdentityFailure.UnsupportedMember);
        }
        if (!_conditions.TryGetValue(condition, out var source))
        {
            source = new NativeConditionSource(condition);
            _conditions.Add(condition, source);
        }

        return source;
    }

    internal INativeIdentityTagSource WrapTag(string key, TagState tag)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (tag == null) throw new ArgumentNullException(nameof(tag));
        if (tag.GetType() != typeof(TagState))
        {
            throw new NativeItemIdentityReader.UnsupportedIdentityException(
                NativeItemIdentityFailure.UnsupportedMember);
        }
        if (!_tags.TryGetValue(tag, out var source))
        {
            source = new NativeTagSource(key, tag);
            _tags.Add(tag, source);
        }
        else if (!string.Equals(source.Key, key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A native tag state is shared by different dictionary keys.");
        }

        return source;
    }

    internal INativeIdentityShapeSource? WrapShape(GridShape? shape)
    {
        if (shape == null) return null;
        var type = shape.GetType();
        if (type != typeof(GridShapeBuilder))
        {
            throw new NativeItemIdentityReader.UnsupportedIdentityException(
                NativeItemIdentityFailure.UnsupportedMember);
        }

        if (!_shapes.TryGetValue(shape, out var source))
        {
            source = new NativeShapeSource(shape);
            _shapes.Add(shape, source);
        }

        return source;
    }

    private sealed class NativeItemSource : INativeIdentityItemSource
    {
        private readonly NativeIdentityAdapter _owner;
        private readonly GameItem _item;
        internal NativeItemSource(NativeIdentityAdapter owner, GameItem item) { _owner = owner; _item = item; }
        public NativeIdentityItemKind Kind => _item.GetType() == typeof(GameItemElement)
            ? NativeIdentityItemKind.GameItemElement : NativeIdentityItemKind.Unsupported;
        public string Identifier => _item.identifier;
        public string? Name => _item.name;
        public string? ShortDescription => _item.shortDescription;
        public string? FlavorText => _item.flavorText;
        public long UnitBaseValue => _item.unitBaseValue;
        public long UnitValue => _item.unitValue;
        public long LateUnitValue => _item.lateUnitValue;
        public long BackupUnitValue => _item.backupUnitValue;
        public IReadOnlyList<string> ItemTypes
        {
            get
            {
                var values = _item.itemTypes;
                if (values == null || values.Count > 1048576 / 5)
                {
                    throw new NativeItemIdentityReader.UnsupportedIdentityException(
                        NativeItemIdentityFailure.InvalidNativeState);
                }

                for (var index = 0; index < values.Count; index++)
                {
                    NativeItemIdentityReader.ValidateNativeString(values[index]);
                }

                return values;
            }
        }
        public string? SpriteAtlasPath => _item.spriteAtlasPath;
        public string? SpritePath => _item.spritePath;
        public int UnitCount => _item.unitCount;
        public int UniqueId => _item.uniqueId;
        public INativeIdentityInventorySource? ParentInventory => _item.parentInventory == null ? null : _owner.WrapInventory(_item.parentInventory);
        public INativeIdentityShapeSource? Shape => _owner.WrapShape(_item.shape);
        public INativeIdentityShapeSource? ModifiedShape => _owner.WrapShape(_item.modifiedShape);
        public IReadOnlyList<INativeIdentityTagSource> Tags =>
            _item.state?.dict == null ? throw new InvalidOperationException("The native state dictionary is unavailable.") :
            CopyTags(_item.state.dict);

        private IReadOnlyList<INativeIdentityTagSource> CopyTags(
            IReadOnlyDictionary<string, TagState> values)
        {
            if (values.Count > NativeItemIdentityReader.MaxRecords)
            {
                throw new NativeItemIdentityReader.UnsupportedIdentityException(
                    NativeItemIdentityFailure.InvalidNativeState);
            }

            foreach (var pair in values)
            {
                NativeItemIdentityReader.ValidateNativeString(pair.Key);
                if (pair.Value == null)
                {
                    throw new NativeItemIdentityReader.StaleIdentityException(
                        NativeItemIdentityFailure.ReadException);
                }
            }

            return values.Select(pair => _owner.WrapTag(pair.Key, pair.Value)).ToArray();
        }
        public IReadOnlyList<INativeIdentityFeatureSource> Features =>
            _item.itemFeatures == null ? throw new InvalidOperationException("The native feature list is unavailable.") :
            CopyFeatures(_item.itemFeatures);

        private IReadOnlyList<INativeIdentityFeatureSource> CopyFeatures(IReadOnlyList<ItemFeature> values)
        {
            if (values.Count > NativeItemIdentityReader.MaxRecords)
            {
                throw new NativeItemIdentityReader.UnsupportedIdentityException(
                    NativeItemIdentityFailure.InvalidNativeState);
            }

            return values.Select(_owner.WrapFeature).ToArray();
        }
        public INativeIdentityWindowSource? ContentWindow => _item.contentWindow == null ? null : _owner.WrapWindow(_item.contentWindow);
        public INativeIdentityWindowSource? ExamineWindow => _item.examineWindow == null ? null : _owner.WrapWindow(_item.examineWindow);
    }

    private sealed class NativeTagSource : INativeIdentityTagSource
    {
        private readonly string _key;
        private readonly TagState _tag;
        internal NativeTagSource(string key, TagState tag) { _key = key; _tag = tag; }
        public string? Key => _key;
        public string? Identifier => _tag.identifier;
        public string? IdentifierName => _tag.identifierName;
        public bool Enabled => _tag.valueEnabled;
        public string? ValueString => _tag.valueString;
        public int ValueInt => _tag.valueInt;
        public uint ValueFloatBits => BitConverter.ToUInt32(BitConverter.GetBytes(_tag.valueFloat), 0);
        public long ValueLong => _tag.valueLong;
        public ulong ValueDoubleBits => unchecked((ulong)BitConverter.DoubleToInt64Bits(_tag.valueDouble));
        public bool ValueBool => _tag.valueBool;
    }

    private sealed class NativeConditionSource : INativeIdentityConditionSource
    {
        private readonly ItemCondition _condition;
        internal NativeConditionSource(ItemCondition condition) { _condition = condition; }
        public string? Display => _condition.display;
        public string? Identifier => _condition.identifier;
        public int ModValue => _condition.modValue;
        public int ModValueDisplay => _condition.modValueDisplay;
        public bool IsTransformative => _condition.isTransformative;
        public string? Category => _condition.category;
        public bool HiddenAsPublic => _condition.hiddenAsPublic;
        public int CustomValue => _condition.customValue;
        public string? OnActivateActionId => _condition.onActivateActionId;
        public int CompareValue => _condition.compareValue;
    }

    private sealed class NativeFeatureSource : INativeIdentityFeatureSource
    {
        private readonly NativeIdentityAdapter _owner;
        private readonly ItemFeature _feature;
        private static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal NativeFeatureSource(NativeIdentityAdapter owner, ItemFeature feature) { _owner = owner; _feature = feature; }
        public string? Identifier => _feature.identifier;
        public string? Category => _feature.category;
        public int FeatureType => Convert.ToInt32(_feature.featureType);
        public int ValueStage => Convert.ToInt32(_feature.valueStage);
        public int OwnerUniqueId => _feature.parentItemUniqueId;
        public bool IsExposing => _feature.isExposing;
        public INativeIdentityConditionSource? FakeCondition => _feature.fakeCondition == null ? null : _owner.WrapCondition(_feature.fakeCondition);
        public INativeIdentityConditionSource? RealCondition => _feature.realCondition == null ? null : _owner.WrapCondition(_feature.realCondition);
        public bool UseCondition => _feature.useCondition;
        public bool IsPublicHidden => _feature.isPublicHidden;
        public int CustomIntValue1 => _feature.customIntValue1;
        public bool IsDisabled => _feature.isDisabled;
        public bool IsFeatureMatch => _feature.isFeatureMatch;
        public string? PublicDisplay => Read<string>("publicDisplay");
        public bool PublicDisplayUnavailable => false;
        public NativeIdentityFeaturePrivateFields? PrivateFields
        {
            get
            {
        var result = new NativeIdentityFeaturePrivateFields(
            Read<string>("publicDisplay"), Read<string>("actualDisplay"), Read<string>("removedByTool"),
            Read<int>("valueModifier"), Read<bool>("initiallyShown"), Read<bool>("isFeatureExposed"),
            Read<bool>("isFeatureDiscovered"), Read<bool>("usePreExposeValue"),
            Read<int>("preExposeValueModifier"), Read<bool>("isExposable"), null);
        return result;
            }
        }

        private T? Read<T>(string name)
        {
            if (name == "__missing__") return default;
            var field = typeof(ItemFeature).GetField(name, InstanceFields);
            if (field == null || field.DeclaringType != typeof(ItemFeature) || field.FieldType != typeof(T))
            {
                throw new NativeItemIdentityReader.UnsupportedIdentityException(
                    NativeItemIdentityFailure.UnsupportedMember);
            }

            return (T?)field.GetValue(_feature);
        }
    }

    private sealed class NativeShapeSource : INativeIdentityShapeSource
    {
        private readonly GridShape _shape;
        private readonly int _backingLength;
        internal NativeShapeSource(GridShape shape)
        {
            _shape = shape;
            var field = typeof(GridShapeBuilder).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.DeclaringType != typeof(GridShapeBuilder) || field.FieldType != typeof(byte[]))
            {
                throw new NativeItemIdentityReader.UnsupportedIdentityException(
                    NativeItemIdentityFailure.UnsupportedMember);
            }

            var data = field.GetValue(shape) as byte[];
            if (data == null)
            {
                throw new NativeItemIdentityReader.StaleIdentityException(
                    NativeItemIdentityFailure.ReadException);
            }

            _backingLength = data.Length;
        }
        public NativeIdentityShapeKind Kind => _shape.GetType() == typeof(GridShapeBuilder)
            ? NativeIdentityShapeKind.GridShapeBuilder : NativeIdentityShapeKind.Unsupported;
        public int MinX => _shape.minX;
        public int MinY => _shape.minY;
        public int Width => _shape.width;
        public int Height => _shape.height;
        public bool Flipped => _shape.flipped;
        public int Orientation => _shape.orientation;
        public byte Outside => _shape.GetOutsideBounds();
        public int BackingLength => _backingLength;
        public byte GetLocal(int x, int y) => _shape.GetLocal(x, y);
    }

    private sealed class NativeWindowSource : INativeIdentityWindowSource
    {
        private readonly NativeIdentityAdapter _owner;
        private readonly PixelWindow _window;
        internal NativeWindowSource(NativeIdentityAdapter owner, PixelWindow window) { _owner = owner; _window = window; }
        public IReadOnlyList<INativeIdentityItemSource> Parents
        {
            get
            {
                if (_window.parentItems == null || _window.parentItems.Count > NativeItemIdentityReader.MaxItems)
                {
                    throw new NativeItemIdentityReader.UnsupportedIdentityException(
                        NativeItemIdentityFailure.InvalidNativeState);
                }

                return _window.parentItems.Select(_owner.WrapItem).ToArray();
            }
        }
        public IReadOnlyList<INativeIdentityGraphSource> Children
        {
            get
            {
                if (_window.childElement == null)
                {
                    return Array.Empty<INativeIdentityGraphSource>();
                }

                if (_window.childElement is not GraphNodeStorage storage)
                {
                    throw new NativeItemIdentityReader.UnsupportedIdentityException(
                        NativeItemIdentityFailure.UnsupportedGraph);
                }

                return new[] { _owner.WrapGraph(storage) };
            }
        }
    }

    private sealed class NativeGraphSource : INativeIdentityGraphSource
    {
        private readonly NativeIdentityAdapter _owner;
        private readonly GraphNodeStorage _graph;
        internal NativeGraphSource(NativeIdentityAdapter owner, GraphNodeStorage graph) { _owner = owner; _graph = graph; }
        public NativeIdentityGraphKind Kind => _graph is GameInventory ? NativeIdentityGraphKind.Inventory :
            _graph is GameItem ? NativeIdentityGraphKind.Item : NativeIdentityGraphKind.Unsupported;
        public INativeIdentityItemSource? Item => _graph is GameItem item ? _owner.WrapItem(item) : null;
        public INativeIdentityInventorySource? Inventory => _graph is GameInventory inventory ? _owner.WrapInventory(inventory) : null;
    }

    private sealed class NativeInventorySource : INativeIdentityInventorySource
    {
        private readonly NativeIdentityAdapter _owner;
        private readonly GameInventory _inventory;
        internal NativeInventorySource(NativeIdentityAdapter owner, GameInventory inventory) { _owner = owner; _inventory = inventory; }
        public NativeIdentityInventoryKind Kind => _inventory.GetType() == typeof(GameGridInventory) ? NativeIdentityInventoryKind.Grid :
            _inventory.GetType() == typeof(GameGridScrollableInventory) ? NativeIdentityInventoryKind.Scrollable :
            _inventory.GetType() == typeof(GameSlotInventory) ? NativeIdentityInventoryKind.Slot : NativeIdentityInventoryKind.Unsupported;
        public INativeIdentityWindowSource? ParentWindow => _inventory.parent is PixelWindow window ? _owner.WrapWindow(window) : null;
        public IReadOnlyList<INativeIdentityItemSource> Items => CopyItems();
        public IReadOnlyList<INativeIdentityGraphSource> Children => CopyChildren();
        public INativeIdentityItemSource? SlotItem => _inventory is GameSlotInventory slot && slot.childItem != null ? _owner.WrapItem(slot.childItem) : null;
        public IReadOnlyList<INativeIdentityGraphSource> SlotChildren =>
            _inventory is GameSlotInventory ? CopyAllChildren() : Array.Empty<INativeIdentityGraphSource>();
        public INativeIdentityShapeSource? Shape => _inventory is GameGridInventory grid ? _owner.WrapShape(grid.inventoryShape) : null;
        public int Width => _inventory is GameGridScrollableInventory scroll ? scroll.width : 0;
        public int Height => _inventory is GameGridScrollableInventory scroll ? scroll.height : 0;
        public bool AlwaysStack => _inventory is GameGridScrollableInventory scroll && scroll.alwaysStack;
        public int Orientation => _inventory is GameSlotInventory slot ? slot.defaultOrientation : -1;

        private IReadOnlyList<INativeIdentityItemSource> CopyItems()
        {
            if (_inventory is GameSlotInventory slot)
            {
                var item = slot.childItem;
                return item == null ? Array.Empty<INativeIdentityItemSource>() : new[] { _owner.WrapItem(item) };
            }

            var items = ReadItemField();
            return items.Select(_owner.WrapItem).ToArray();
        }

        private IReadOnlyList<INativeIdentityGraphSource> CopyChildren()
        {
            if (_inventory is GameSlotInventory)
            {
                EnsureSlotHasNoStolenUi();
                return Array.Empty<INativeIdentityGraphSource>();
            }

            var items = ReadItemField();
            return items.Select(item => _owner.WrapGraph(item)).ToArray();
        }

        private IReadOnlyList<INativeIdentityGraphSource> CopyAllChildren()
        {
            if (!(_inventory is GameSlotInventory slot))
            {
                return Array.Empty<INativeIdentityGraphSource>();
            }

            EnsureSlotHasNoStolenUi();
            var item = slot.childItem;
            return item == null ? Array.Empty<INativeIdentityGraphSource>() :
                new[] { _owner.WrapGraph(item) };
        }

        // The mod scanner classifies a reflection lookup by tracing its receiver back to a literal type
        // (typeof(...) or a literal type name, also through a local assigned once). _inventory.GetType() cannot be
        // traced, so the scanner treats it as a computed type requiring reflection-external. Each grid type
        // therefore gets its own literal lookup. childItems would avoid reflection but copies the list on every read.
        private static readonly FieldInfo? GridItems =
            typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? ScrollableItems =
            typeof(GameGridScrollableInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic);

        private List<GameItem> ReadItemField()
        {
            // Exact types only, as before: a subclass may keep its items elsewhere.
            var type = _inventory.GetType();
            var field = type == typeof(GameGridInventory) ? GridItems
                : type == typeof(GameGridScrollableInventory) ? ScrollableItems
                : null;
            if (field == null || field.DeclaringType != type || field.FieldType != typeof(List<GameItem>))
            {
                throw new NativeItemIdentityReader.UnsupportedIdentityException(
                    NativeItemIdentityFailure.UnsupportedMember);
            }

            var items = field.GetValue(_inventory) as List<GameItem>;
            if (items == null || items.Count < 0 || items.Count > NativeItemIdentityReader.MaxItems)
            {
                throw new NativeItemIdentityReader.UnsupportedIdentityException(
                    NativeItemIdentityFailure.InvalidNativeState);
            }

            return items;
        }

        private void EnsureSlotHasNoStolenUi()
        {
            var type = typeof(GameSlotInventory);
            var field = type.GetField("stolenUI", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.DeclaringType != type || field.FieldType != typeof(PixelElement))
            {
                throw new NativeItemIdentityReader.UnsupportedIdentityException(
                    NativeItemIdentityFailure.UnsupportedMember);
            }

            if (field.GetValue(_inventory) != null)
            {
                throw new NativeItemIdentityReader.UnsupportedIdentityException(
                    NativeItemIdentityFailure.UnsupportedGraph);
            }
        }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
