using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CanonicalIdentityBoundaryTests
{
    [TestCase(65536)]
    [TestCase(65537)]
    public void ItemTypes_AllowedCountsAreNotConflatedWithEdgeLimit(int count)
    {
        var types = Enumerable.Repeat(Array.Empty<byte>(), count).ToArray();
        AssertValid(Root(itemTypes: types));
    }

    [Test]
    public void ItemTypes_DeclaredCountExceedingRemainingBytes_Rejects()
    {
        var bytes = Root();
        SetU32(bytes, 51, uint.MaxValue);
        AssertInvalid(bytes);
    }

    [Test]
    public void StringAndEnvelope_ExactBoundariesAndOverflow()
    {
        AssertValid(Root(name: new string('x', 65536)));
        AssertInvalid(Root(name: new string('x', 65537)));

        // Each type contributes one marker and four length bytes; the name makes the total exact.
        var types = Enumerable.Repeat(Array.Empty<byte>(), 200000).ToArray();
        var baseBytes = Root(itemTypes: types, name: string.Empty);
        var nameLength = 1048576 - baseBytes.Length;
        var exact = Root(itemTypes: types, name: new string('x', nameLength));
        var oversized = Root(itemTypes: types, name: new string('x', nameLength + 1));
        Assert.That(exact.Length, Is.EqualTo(1048576));
        Assert.That(oversized.Length, Is.EqualTo(1048577));
        AssertValid(exact);
        AssertInvalid(oversized);
    }

    [TestCase(16, true)]
    [TestCase(17, false)]
    public void Nodes_DepthBoundary(int depth, bool valid)
    {
        byte[] node = Window();
        for (var i = 1; i < depth; i++) node = Window(node);
        AssertValidity(Root(content: node), valid);
    }

    [TestCase(8192, true)]
    [TestCase(8193, false)]
    public void Nodes_CountBoundary(int count, bool valid)
    {
        var children = Enumerable.Repeat(Window(), count - 1).ToArray();
        AssertValidity(Root(content: Window(children)), valid);
    }

    [TestCase(8, true)]
    [TestCase(9, false)]
    public void Items_DepthBoundary(int depth, bool valid)
    {
        byte[]? next = null;
        for (var i = 0; i < depth; i++) next = Slot(Child(content: next));
        AssertValidity(Root(content: next), valid);
    }

    [TestCase(8191, true)]
    [TestCase(8192, false)]
    public void Items_CountBoundary(int children, bool valid)
    {
        var input = Root(content: Grid(Enumerable.Repeat(Child(), children).ToArray()));
        Assert.That(input.Length, Is.LessThanOrEqualTo(1048576));
        AssertValidity(input, valid);
    }

    [Test]
    public void Records_OverLimitDeclaredCountRejectsWithoutAllocation()
    {
        var bytes = Root();
        SetU32(bytes, 57, 65537);
        AssertInvalid(bytes);
    }

    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(255)]
    public void ReservedNodeKinds_Reject(byte kind) => AssertInvalid(Root(content: new[] { kind }));

    [Test]
    public void BooleanMarkers_InWindowShapeSlotAndTagReject()
    {
        var root = Root(); root[^2] = 2; AssertInvalid(root);
        var shape = Shape(); shape[16] = 2; AssertInvalid(Root(content: GridWithShape(shape)));
        var slot = Slot(null); slot[5] = 2; AssertInvalid(Root(content: slot));
        var tag = Tag("a", true, 0); tag[8] = 2; AssertInvalid(Root(tags: new[] { tag }));
        var feature = Feature("x", "", Array.Empty<byte>()); feature[19] = 2; AssertInvalid(Root(features: new[] { feature }));
        var optionalCondition = Feature("x", "", Array.Empty<byte>()); optionalCondition[23] = 2; AssertInvalid(Root(features: new[] { optionalCondition }));
        var condition = Feature("x", "", Array.Empty<byte>(), includeCondition: true); AssertValid(Root(features: new[] { condition }));
        condition[34] = 2; AssertInvalid(Root(features: new[] { condition }));
    }

    [Test]
    public void DoubleBits_NonfiniteRejectedAndSignedZeroPreserved()
    {
        var plus = Root(tags: new[] { Tag("a", true, 0) });
        var minus = Root(tags: new[] { Tag("a", true, 0x8000000000000000UL) });
        Assert.That(ItemKey.FromCanonical("a", plus), Is.Not.EqualTo(ItemKey.FromCanonical("a", minus)));
        AssertInvalid(Root(tags: new[] { Tag("a", true, 0x7ff0000000000000UL) }));
        AssertInvalid(Root(tags: new[] { Tag("a", true, 0x7ff8000000000001UL) }));
        AssertInvalid(Root(tags: new[] { Tag("a", false, 0x8000000000000000UL) }));
    }

    [Test]
    public void DescendantFeatures_RejectDuplicateIdsAndForbiddenCategory_AcceptAction()
    {
        var rootFeature = Feature("same", "", Array.Empty<byte>());
        var childPath = new byte[] { 1, 3, 0, 0, 0, 0, 4, 0, 0, 0, 0 };
        var duplicate = Feature("same", "", childPath);
        AssertValid(Root(features: new[] { rootFeature }, content: Window(Grid(Child(features: new[] { Feature("other", "", childPath) })))));
        AssertInvalid(Root(features: new[] { rootFeature }, content: Window(Grid(Child(features: new[] { duplicate })))));
        AssertInvalid(Root(content: Window(Grid(Child(features: new[] { Feature("x", "contrabandMarkUp", childPath) })))));
        AssertValid(Root(content: Window(Grid(Child(features: new[] { Feature("x", "", childPath, action: "genuineInjector") })))));
    }

    [Test]
    public void FeatureOwner_PathToDifferentExistingItem_Rejects()
    {
        var pathToFirst = new byte[] { 1, 3, 0, 0, 0, 0, 4, 0, 0, 0, 0 };
        var pathToSecond = new byte[] { 1, 3, 0, 0, 0, 0, 4, 0, 0, 0, 1 };
        AssertValid(Root(content: Window(Grid(Child(), Child(features: new[] { Feature("x", "", pathToSecond) })))));
        AssertInvalid(Root(content: Window(Grid(Child(), Child(features: new[] { Feature("x", "", pathToFirst) })))));
    }

    private static void AssertValid(byte[] bytes) => Assert.That(ItemKey.FromCanonical("a", bytes).IsCanonical, Is.True);
    private static void AssertInvalid(byte[] bytes) => Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", bytes)));
    private static void AssertValidity(byte[] bytes, bool valid) { if (valid) AssertValid(bytes); else AssertInvalid(bytes); }

    private static byte[] Root(byte[][]? itemTypes = null, string? name = null, byte[][]? tags = null,
        byte[][]? features = null, byte[]? content = null) => Item(false, itemTypes, name, tags, features, content);
    private static byte[] Child(byte[][]? features = null, byte[]? content = null) => Item(true, null, null, null, features, content);
    private static byte[] Item(bool child, byte[][]? itemTypes, string? name, byte[][]? tags, byte[][]? features, byte[]? content)
    {
        var b = new List<byte>();
        if (!child) { b.AddRange(Encoding.ASCII.GetBytes("DSID")); U32(b, 1); }
        b.Add(child ? (byte)11 : (byte)10);
        if (child) { U32(b, 1); b.AddRange(Shape()); b.AddRange(Shape()); }
        b.Add(1); S(b, "a"); S(b, name); S(b, null); S(b, null); b.AddRange(new byte[32]);
        itemTypes ??= Array.Empty<byte[]>(); U32(b, (uint)itemTypes.Length);
        foreach (var value in itemTypes) { b.Add(1); U32(b, (uint)value.Length); b.AddRange(value); }
        S(b, null); S(b, null);
        tags ??= Array.Empty<byte[]>(); U32(b, (uint)tags.Length); foreach (var tag in tags) b.AddRange(tag);
        features ??= Array.Empty<byte[]>(); U32(b, (uint)features.Length); foreach (var feature in features) b.AddRange(feature);
        b.Add(content == null ? (byte)0 : (byte)1); if (content != null) b.AddRange(content); b.Add(0);
        return b.ToArray();
    }
    private static byte[] Window(params byte[][] nodes) { var b = new List<byte> { 1 }; U32(b, (uint)nodes.Length); foreach (var n in nodes) b.AddRange(n); return b.ToArray(); }
    private static byte[] Grid(params byte[][] items) { var b = new List<byte> { 2 }; b.AddRange(Shape()); U32(b, (uint)items.Length); foreach (var i in items) b.AddRange(i); return b.ToArray(); }
    private static byte[] GridWithShape(byte[] shape) { var b = new List<byte> { 2 }; b.AddRange(shape); U32(b, 0); return b.ToArray(); }
    private static byte[] Slot(byte[]? item) { var b = new List<byte> { 4 }; U32(b, 0); b.Add(item == null ? (byte)0 : (byte)1); if (item != null) b.AddRange(item); U32(b, 0); return b.ToArray(); }
    private static byte[] Shape() { var b = new List<byte>(); U32(b, 0); U32(b, 0); U32(b, 1); U32(b, 1); b.Add(0); U32(b, 0); b.Add(0); U32(b, 1); b.Add(0); return b.ToArray(); }
    private static byte[] Tag(string key, bool enabled, ulong doubleBits)
    {
        var b = new List<byte>(); S(b, key); S(b, null); S(b, null); b.Add(enabled ? (byte)1 : (byte)0); S(b, null);
        U32(b, 0); U32(b, 0); b.AddRange(new byte[8]); U64(b, doubleBits); b.Add(0); return b.ToArray();
    }
    private static byte[] Feature(string id, string category, byte[] path, string? action = null, bool includeCondition = false)
    {
        var b = new List<byte>(); U32(b, 0); U32(b, 0); U32(b, path.Length == 0 ? 0U : 3U); b.AddRange(path);
        S(b, null); S(b, null); S(b, null); U32(b, 0); b.AddRange(new byte[4]);
        var hasCondition = action != null || includeCondition;
        b.Add(hasCondition ? (byte)1 : (byte)0);
        if (hasCondition) { S(b, null); S(b, null); U32(b, 0); U32(b, 0); b.Add(0); S(b, null); b.Add(0); U32(b, 0); S(b, action); U32(b, 0); }
        b.Add(0); b.Add(0); b.Add(0); U32(b, 0); b.Add(0); b.Add(0); U32(b, 0); b.Add(0); S(b, id); S(b, category); b.Add(0);
        return b.ToArray();
    }
    private static void S(List<byte> b, string? value) { if (value == null) { b.Add(0); return; } var bytes = Encoding.UTF8.GetBytes(value); b.Add(1); U32(b, (uint)bytes.Length); b.AddRange(bytes); }
    private static void U32(List<byte> b, uint value) { b.Add((byte)(value >> 24)); b.Add((byte)(value >> 16)); b.Add((byte)(value >> 8)); b.Add((byte)value); }
    private static void U64(List<byte> b, ulong value) { U32(b, (uint)(value >> 32)); U32(b, (uint)value); }
    private static void SetU32(byte[] b, int offset, uint value) { b[offset] = (byte)(value >> 24); b[offset + 1] = (byte)(value >> 16); b[offset + 2] = (byte)(value >> 8); b[offset + 3] = (byte)value; }
}
