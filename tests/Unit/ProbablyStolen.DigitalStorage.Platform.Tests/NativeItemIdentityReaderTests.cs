using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Platform.Identity;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
public sealed class NativeItemIdentityReaderTests
{
    [Test]
    public void Read_LeafDouble_ProducesTheSameCanonicalIdentityAsDetachedProjection()
    {
        var root = CreateItem("root", 1);
        root.Tags.Add(new Tag("state", enabled: true, valueInt: 4));

        var result = NativeItemIdentityReader.Read(root);
        var expected = CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(
            "root", Array.Empty<string>(), new[] { new CanonicalIdentityTag("state", "state", "state", true, valueInt: 4) },
            Array.Empty<CanonicalIdentityFeature>()));

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.None));
        Assert.That(result.Identity, Is.EqualTo(expected));
    }

    [Test]
    public void Read_NestedGridAndFeature_PreservesOwnerAndGeometryProjection()
    {
        var root = CreateItem("root", 1);
        var child = CreateItem("child", 2);
        child.Features.Add(new Feature("feature", "", 2)
        {
            OwnerUniqueId = 2,
            PrivateFields = PrivateFields("display")
        });
        var grid = new DoubleInventory(NativeIdentityInventoryKind.Grid)
        {
            Shape = CreateShape(1, 1, new byte[] { 0 })
        };
        var window = new DoubleWindow(root);
        grid.ParentWindow = window;
        grid.Items.Add(child);
        child.ParentInventory = grid;
        grid.Children.Add(new DoubleItemGraph(child));
        window.Children.Add(new DoubleGraph(grid));
        root.ContentWindow = window;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(result.Identity!.IsCanonical, Is.True);
        Assert.That(result.Identity.CopyCanonicalState(), Is.Not.Empty);
        Assert.That(child.Features[0].OwnerUniqueId, Is.EqualTo(2));
        Assert.That(grid.Items.Single(), Is.SameAs(child));
    }

    [Test]
    public void Read_UnknownItemKind_RejectsWithoutEmittingAnIdentity()
    {
        var root = CreateItem("root", 1);
        root.Kind = NativeIdentityItemKind.Unsupported;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.UnsupportedType));
        Assert.That(result.Identity, Is.Null);
    }

    [Test]
    public void Read_MismatchedBidirectionalMembership_IsStale()
    {
        var root = CreateItem("root", 1);
        var child = CreateItem("child", 2);
        var grid = new DoubleInventory(NativeIdentityInventoryKind.Grid)
        {
            Shape = CreateShape(1, 1, new byte[] { 0 })
        };
        var window = new DoubleWindow(root);
        grid.ParentWindow = window;
        grid.Items.Add(child);
        child.ParentInventory = null;
        grid.Children.Add(new DoubleItemGraph(child));
        window.Children.Add(new DoubleGraph(grid));
        root.ContentWindow = window;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Stale));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.OwnershipMismatch));
    }

    [Test]
    public void Read_SharedShapeReference_IsRejectedAsSharedState()
    {
        var root = CreateItem("root", 1);
        var shared = CreateShape(1, 1, new byte[] { 0 });
        var child = CreateItem("child", 2);
        child.Shape = shared;
        child.ModifiedShape = shared;
        var grid = new DoubleInventory(NativeIdentityInventoryKind.Grid)
        {
            Shape = CreateShape(1, 1, new byte[] { 0 })
        };
        var window = new DoubleWindow(root);
        grid.ParentWindow = window;
        grid.Items.Add(child);
        child.ParentInventory = grid;
        grid.Children.Add(new DoubleItemGraph(child));
        window.Children.Add(new DoubleGraph(grid));
        root.ContentWindow = window;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Stale));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.SharedReference));
    }

    [Test]
    public void Read_CyclicWindowGraph_IsRejectedAsSharedState()
    {
        var root = CreateItem("root", 1);
        var child = CreateItem("child", 2);
        var grid = new DoubleInventory(NativeIdentityInventoryKind.Grid)
        {
            Shape = CreateShape(1, 1, new byte[] { 0 })
        };
        var window = new DoubleWindow(root);
        grid.ParentWindow = window;
        grid.Items.Add(child);
        child.ParentInventory = grid;
        grid.Children.Add(new DoubleItemGraph(child));
        window.Children.Add(new DoubleGraph(grid));
        root.ContentWindow = window;
        child.ContentWindow = window;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Stale));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.Cycle));
    }

    [Test]
    public void Read_ConditionActionId_IsPartOfIdentity()
    {
        // Injector conditions such as "counterfeitInjector" carry an action ID; reading it must not reject the item.
        var counterfeit = NativeItemIdentityReader.Read(ItemWithConditionAction("counterfeitInjector"));
        var counterfeitAgain = NativeItemIdentityReader.Read(ItemWithConditionAction("counterfeitInjector"));
        var unusable = NativeItemIdentityReader.Read(ItemWithConditionAction("unusableInjector"));

        Assert.That((counterfeit.Status, counterfeit.Failure), Is.EqualTo((NativeItemIdentityReadStatus.Success, NativeItemIdentityFailure.None)));
        Assert.That(counterfeitAgain.Identity, Is.EqualTo(counterfeit.Identity));
        Assert.That(unusable.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(unusable.Identity, Is.Not.EqualTo(counterfeit.Identity));
    }

    private static DoubleItem ItemWithConditionAction(string action)
    {
        var root = CreateItem("injector", 1);
        root.Features.Add(new Feature("feature", "")
        {
            OwnerUniqueId = 1,
            FakeCondition = new Condition { OnActivateActionId = action },
            PrivateFields = PrivateFields("display")
        });
        return root;
    }

    [Test]
    public void Read_InvalidShapeMask_IsUnsupported()
    {
        var root = CreateItem("root", 1);
        var child = CreateItem("child", 2);
        child.Shape = CreateShape(1, 1, new byte[] { 2 });
        var grid = new DoubleInventory(NativeIdentityInventoryKind.Grid)
        {
            Shape = CreateShape(1, 1, new byte[] { 0 })
        };
        var window = new DoubleWindow(root);
        grid.ParentWindow = window;
        grid.Items.Add(child);
        child.ParentInventory = grid;
        grid.Children.Add(new DoubleItemGraph(child));
        window.Children.Add(new DoubleGraph(grid));
        root.ContentWindow = window;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.InvalidNativeState));
    }

    [Test]
    public void Read_FeatureWithForeignOwner_IsStale()
    {
        var root = CreateItem("root", 1);
        root.Features.Add(new Feature("feature", "")
        {
            OwnerUniqueId = 99,
            PrivateFields = PrivateFields("display")
        });

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Stale));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.OwnershipMismatch));
    }

    [Test]
    public void Read_MissingPrivateProjection_IsUnsupported()
    {
        var root = CreateItem("root", 1);
        root.Features.Add(new Feature("feature", "") { OwnerUniqueId = 1 });

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.UnsupportedMember));
    }

    [Test]
    public void Read_ItemTypeCountAboveRecordLimit_IsAcceptedWithinEnvelope()
    {
        var root = CreateItem("root", 1);
        root.ItemTypes = Enumerable.Repeat(string.Empty, 65537).ToArray();

        var result = NativeItemIdentityReader.Read(root);
        var expected = CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput("root", root.ItemTypes,
            Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>()));

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(result.Identity, Is.EqualTo(expected));
    }

    [Test]
    public void Read_NestedFeaturePath_AllowsExactEnvelopeLimit()
    {
        var root = CreateItem("root", 1);
        var child = CreateItem("child", 2);
        child.Features.Add(new Feature("feature", "", 2)
        {
            OwnerUniqueId = 2,
            PrivateFields = PrivateFields("display")
        });
        var grid = new DoubleInventory(NativeIdentityInventoryKind.Grid)
        {
            Shape = CreateShape(1, 1, new byte[] { 0 })
        };
        var window = new DoubleWindow(root);
        grid.ParentWindow = window;
        grid.Items.Add(child);
        child.ParentInventory = grid;
        grid.Children.Add(new DoubleItemGraph(child));
        window.Children.Add(new DoubleGraph(grid));
        root.ContentWindow = window;

        var baseline = NativeItemIdentityReader.Read(root);
        Assert.That(baseline.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        var remaining = 1048576 - baseline.Identity!.CopyCanonicalState().Length - 16 * 5 - 15 * 65536;
        Assert.That(remaining, Is.InRange(0, 65536));
        root.ItemTypes = Enumerable.Repeat(new string('x', 65536), 15)
            .Append(new string('x', remaining)).ToArray();

        var result = NativeItemIdentityReader.Read(root);
        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success), result.Failure.ToString());
        Assert.That(result.Identity!.CopyCanonicalState().Length, Is.EqualTo(1048576));

        root.ItemTypes = root.ItemTypes.Append(string.Empty).ToArray();
        Assert.That(NativeItemIdentityReader.Read(root).Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
    }

    [Test]
    public void Read_PresentCondition_AllowsExactEnvelopeLimit()
    {
        var root = CreateItem("root", 1);
        root.Features.Add(new Feature("feature", "", 2)
        {
            OwnerUniqueId = 1,
            FakeCondition = new Condition(),
            PrivateFields = PrivateFields("display")
        });

        var baseline = NativeItemIdentityReader.Read(root);
        Assert.That(baseline.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        var remaining = 1048576 - baseline.Identity!.CopyCanonicalState().Length - 16 * 5 - 15 * 65536;
        Assert.That(remaining, Is.InRange(0, 65536));
        root.ItemTypes = Enumerable.Repeat(new string('x', 65536), 15)
            .Append(new string('x', remaining)).ToArray();

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success), result.Failure.ToString());
        Assert.That(result.Identity!.CopyCanonicalState().Length, Is.EqualTo(1048576));
    }

    [Test]
    public void Read_CumulativeStringBudget_RejectsBeforeDetachedStringListAllocation()
    {
        var root = CreateItem("root", 1);
        var itemTypes = new List<string>();
        for (var index = 0; index < 17; index++)
        {
            itemTypes.Add(new string('x', 65536));
        }

        root.ItemTypes = itemTypes;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.InvalidNativeState));
        Assert.That(result.Identity, Is.Null);
    }

    [Test]
    public void Read_CumulativeRecordBudget_RejectsBeforeLaterFeatureMaterialization()
    {
        var root = CreateItem("root", 1);
        for (var index = 0; index < 65536; index++)
        {
            root.Tags.Add(new Tag("tag-" + index));
        }

        root.Features.Add(new Feature("feature", "")
        {
            OwnerUniqueId = 1,
            PrivateFields = PrivateFields("display")
        });

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.InvalidNativeState));
        Assert.That(result.Identity, Is.Null);
    }

    [Test]
    public void Read_CumulativeShapeBudget_RejectsBeforeMaskMaterialization()
    {
        var root = CreateItem("root", 1);
        var child = CreateItem("child", 2);
        child.Shape = CreateShape(1024, 1024, new byte[1024 * 1024]);
        child.ModifiedShape = CreateShape(1, 1, new byte[] { 0 });
        var grid = new DoubleInventory(NativeIdentityInventoryKind.Grid)
        {
            Shape = CreateShape(1, 1, new byte[] { 0 })
        };
        var window = new DoubleWindow(root);
        grid.ParentWindow = window;
        grid.Items.Add(child);
        child.ParentInventory = grid;
        grid.Children.Add(new DoubleItemGraph(child));
        window.Children.Add(new DoubleGraph(grid));
        root.ContentWindow = window;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.InvalidNativeState));
        Assert.That(result.Identity, Is.Null);
    }

    [Test]
    public void Read_WindowCountsTowardNodeBudgetBeforeMaterializingChildren()
    {
        var root = CreateItem("root", 1);
        var window = new DoubleWindow(root);
        for (var index = 0; index < 8192; index++)
        {
            var inventory = new DoubleInventory(NativeIdentityInventoryKind.Grid)
            {
                Shape = CreateShape(1, 1, new byte[] { 0 }),
                ParentWindow = window
            };
            window.Children.Add(new DoubleGraph(inventory));
        }

        root.ContentWindow = window;
        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.InvalidNativeState));
        Assert.That(result.Identity, Is.Null);
    }

    [Test]
    public void Read_WindowDepthCountsBeforeReadingItsChildren()
    {
        var root = CreateItem("root", 1);
        var window = new DoubleWindow(root);
        var first = new DoubleInventory(NativeIdentityInventoryKind.Slot) { ParentWindow = window };
        window.Children.Add(new DoubleGraph(first));
        var current = first;
        for (var index = 1; index < 16; index++)
        {
            var next = new DoubleInventory(NativeIdentityInventoryKind.Slot) { ParentWindow = window };
            current.Children.Add(new DoubleGraph(next));
            current = next;
        }

        root.ContentWindow = window;
        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Unsupported));
        Assert.That(result.Failure, Is.EqualTo(NativeItemIdentityFailure.InvalidNativeState));
        Assert.That(result.Identity, Is.Null);
    }

    [Test]
    public void Read_DoesNotMutateDoubleGraph()
    {
        var root = CreateItem("root", 1);
        var originalTags = root.Tags.ToArray();
        var originalFeatures = root.Features.ToArray();
        var originalIdentifier = root.Identifier;

        var result = NativeItemIdentityReader.Read(root);

        Assert.That(result.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(root.Identifier, Is.EqualTo(originalIdentifier));
        Assert.That(root.Tags, Is.EqualTo(originalTags));
        Assert.That(root.Features, Is.EqualTo(originalFeatures));
    }

    [Test]
    public void Read_RootsDifferingOnlyInPositionOrientationFlipOrUniqueId_ShareOneIdentity()
    {
        var upright = CreateItem("rifle", 10);
        upright.Shape = CreateShape(2, 3, new byte[6]);
        upright.ModifiedShape = CreateShape(2, 3, new byte[6]);
        var rotated = CreateItem("rifle", 11);
        rotated.Shape = CreateShape(2, 3, new byte[6]);
        var rotatedShape = CreateShape(2, 3, new byte[6]);
        rotatedShape.Orientation = 1;
        rotatedShape.Flipped = true;
        rotatedShape.MinX = 7;
        rotatedShape.MinY = 4;
        rotated.ModifiedShape = rotatedShape;

        var first = NativeItemIdentityReader.Read(upright);
        var second = NativeItemIdentityReader.Read(rotated);

        Assert.That(first.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(second.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(second.Identity, Is.EqualTo(first.Identity));
    }

    [Test]
    public void Read_ContainersWithDifferentContents_HaveDifferentIdentities()
    {
        var withApple = Container(1, "apple");
        var withPear = Container(3, "pear");

        var first = NativeItemIdentityReader.Read(withApple);
        var second = NativeItemIdentityReader.Read(withPear);

        Assert.That(first.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(second.Status, Is.EqualTo(NativeItemIdentityReadStatus.Success));
        Assert.That(second.Identity, Is.Not.EqualTo(first.Identity));
    }

    private static DoubleItem Container(int uniqueId, string content)
    {
        var root = CreateItem("bag", uniqueId);
        var child = CreateItem(content, uniqueId + 1);
        var grid = new DoubleInventory(NativeIdentityInventoryKind.Grid) { Shape = CreateShape(1, 1, new byte[] { 0 }) };
        var window = new DoubleWindow(root);
        grid.ParentWindow = window;
        grid.Items.Add(child);
        child.ParentInventory = grid;
        grid.Children.Add(new DoubleItemGraph(child));
        window.Children.Add(new DoubleGraph(grid));
        root.ContentWindow = window;
        return root;
    }

    private static DoubleItem CreateItem(string identifier, int uniqueId) => new()
    {
        Identifier = identifier,
        UniqueId = uniqueId,
        Shape = CreateShape(1, 1, new byte[] { 0 }),
        ModifiedShape = CreateShape(1, 1, new byte[] { 0 })
    };

    private static DoubleShape CreateShape(int width, int height, byte[] mask) => new()
    {
        Width = width,
        Height = height,
        Mask = mask,
        Kind = NativeIdentityShapeKind.GridShapeBuilder
    };

    private static NativeIdentityFeaturePrivateFields PrivateFields(string display) =>
        new(display, display, null, 0, true, false, false, false, 0, true, null);

    private sealed class DoubleItem : INativeIdentityItemSource
    {
        public NativeIdentityItemKind Kind { get; set; } = NativeIdentityItemKind.GameItemElement;
        public string Identifier { get; set; } = "";
        public string? Name { get; set; }
        public string? ShortDescription { get; set; }
        public string? FlavorText { get; set; }
        public long UnitBaseValue { get; set; }
        public long UnitValue { get; set; }
        public long LateUnitValue { get; set; }
        public long BackupUnitValue { get; set; }
        public IReadOnlyList<string> ItemTypes { get; set; } = Array.Empty<string>();
        public string? SpriteAtlasPath { get; set; }
        public string? SpritePath { get; set; }
        public int UnitCount { get; set; } = 1;
        public int UniqueId { get; set; }
        public INativeIdentityInventorySource? ParentInventory { get; set; }
        public INativeIdentityShapeSource? Shape { get; set; }
        public INativeIdentityShapeSource? ModifiedShape { get; set; }
        public List<INativeIdentityTagSource> Tags { get; } = new();
        public List<INativeIdentityFeatureSource> Features { get; } = new();
        public INativeIdentityWindowSource? ContentWindow { get; set; }
        public INativeIdentityWindowSource? ExamineWindow { get; set; }
        IReadOnlyList<INativeIdentityTagSource> INativeIdentityItemSource.Tags => Tags;
        IReadOnlyList<INativeIdentityFeatureSource> INativeIdentityItemSource.Features => Features;
    }

    private sealed class DoubleInventory : INativeIdentityInventorySource
    {
        internal DoubleInventory(NativeIdentityInventoryKind kind) { Kind = kind; }
        public NativeIdentityInventoryKind Kind { get; }
        public INativeIdentityWindowSource? ParentWindow { get; set; }
        public List<INativeIdentityItemSource> Items { get; } = new();
        public List<INativeIdentityGraphSource> Children { get; } = new();
        public INativeIdentityItemSource? SlotItem { get; set; }
        public List<INativeIdentityGraphSource> SlotChildren { get; } = new();
        public INativeIdentityShapeSource? Shape { get; set; }
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;
        public bool AlwaysStack { get; set; }
        public int Orientation { get; set; }
        IReadOnlyList<INativeIdentityItemSource> INativeIdentityInventorySource.Items => Items;
        IReadOnlyList<INativeIdentityGraphSource> INativeIdentityInventorySource.Children => Children;
        IReadOnlyList<INativeIdentityGraphSource> INativeIdentityInventorySource.SlotChildren => SlotChildren;
    }

    private sealed class DoubleWindow : INativeIdentityWindowSource
    {
        internal DoubleWindow(INativeIdentityItemSource owner) { Parents.Add(owner); }
        public List<INativeIdentityItemSource> Parents { get; } = new();
        public List<INativeIdentityGraphSource> Children { get; } = new();
        IReadOnlyList<INativeIdentityItemSource> INativeIdentityWindowSource.Parents => Parents;
        IReadOnlyList<INativeIdentityGraphSource> INativeIdentityWindowSource.Children => Children;
    }

    private sealed class DoubleItemGraph : INativeIdentityGraphSource
    {
        internal DoubleItemGraph(DoubleItem item) { Item = item; }
        public NativeIdentityGraphKind Kind => NativeIdentityGraphKind.Item;
        public INativeIdentityItemSource? Item { get; }
        public INativeIdentityInventorySource? Inventory => null;
    }

    private sealed class DoubleGraph : INativeIdentityGraphSource
    {
        internal DoubleGraph(DoubleInventory inventory) { Inventory = inventory; }
        public NativeIdentityGraphKind Kind => NativeIdentityGraphKind.Inventory;
        public INativeIdentityItemSource? Item => null;
        public INativeIdentityInventorySource? Inventory { get; }
    }

    private sealed class DoubleShape : INativeIdentityShapeSource
    {
        public NativeIdentityShapeKind Kind { get; set; }
        public int MinX { get; set; }
        public int MinY { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Flipped { get; set; }
        public int Orientation { get; set; }
        public byte Outside { get; set; }
        public int BackingLength => Mask.Length;
        public byte[] Mask { get; set; } = Array.Empty<byte>();
        public byte GetLocal(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height
            ? Outside : Mask[y * Width + x];
    }

    private sealed class Tag : INativeIdentityTagSource
    {
        internal Tag(string key, bool enabled = false, int valueInt = 0)
        {
            Key = key; Enabled = enabled; ValueInt = valueInt;
        }
        public string? Key { get; }
        public string? Identifier => Key;
        public string? IdentifierName => Key;
        public bool Enabled { get; }
        public string? ValueString => null;
        public int ValueInt { get; }
        public uint ValueFloatBits => 0;
        public long ValueLong => 0;
        public ulong ValueDoubleBits => 0;
        public bool ValueBool => false;
    }

    private sealed class Feature : INativeIdentityFeatureSource
    {
        internal Feature(string identifier, string category, int featureType = 0)
        {
            Identifier = identifier; Category = category; FeatureType = featureType;
        }
        public string? Identifier { get; }
        public string? Category { get; }
        public int FeatureType { get; }
        public int ValueStage { get; set; }
        public int OwnerUniqueId { get; set; }
        public bool IsExposing { get; set; }
        public INativeIdentityConditionSource? FakeCondition { get; set; }
        public INativeIdentityConditionSource? RealCondition { get; set; }
        public bool UseCondition { get; set; }
        public bool IsPublicHidden { get; set; }
        public int CustomIntValue1 { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsFeatureMatch { get; set; }
        public string? PublicDisplay { get; set; }
        public bool PublicDisplayUnavailable { get; set; }
        public NativeIdentityFeaturePrivateFields? PrivateFields { get; set; }
    }

    private sealed class Condition : INativeIdentityConditionSource
    {
        public string? Display { get; set; }
        public string? Identifier { get; set; }
        public int ModValue { get; set; }
        public int ModValueDisplay { get; set; }
        public bool IsTransformative { get; set; }
        public string? Category { get; set; }
        public bool HiddenAsPublic { get; set; }
        public int CustomValue { get; set; }
        public string? OnActivateActionId { get; set; }
        public int CompareValue { get; set; }
    }
}
