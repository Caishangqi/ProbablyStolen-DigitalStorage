using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Api.Core.Tests;

[TestFixture]
public sealed class CanonicalIdentityTests
{
    private const string Leaf = "44534944000000010a01010000000161000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";
    private const string Nested = "44534944000000010a010100000001720000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001010000000102000000000000000000000001000000010000000000000000000100000000010b00000001000000000000000000000001000000010000000000000000000100000000000000000000000001000000010000000000000000000100010100000001630000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000100000000000000000000000301030000000004000000000000000000000000000000000000000000000000000000000000010000000166010000000000000000";
    private const string MultiLevel = "44534944000000010a01010000000172000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001010000000101000000010400000000010b0000000100000000000000000000000100000001000000000000000000010000000000000000000000000100000001000000000000000000010001010000000163000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000101000000010400000000010b0000000100000000000000000000000100000001000000000000000000010000000000000000000000000100000001000000000000000000010001010000000164000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000010000000000000000000000070203000000000300000000050103000000000500000000000000000000000000000000000000000000000000000100000001660100000000000000000000000000000000";

    [TestCase(Leaf, "a")]
    [TestCase(Nested, "r")]
    [TestCase(MultiLevel, "r")]
    public void FromCanonical_ApprovedVectors_AcceptsDetachedState(string hex, string id)
    {
        var input = Convert.FromHexString(hex);
        var original = input.ToArray();
        var key = ItemKey.FromCanonical(id, input);
        input[0] = 0;
        var output = key.CopyCanonicalState();
        Assert.That(key.IsCanonical, Is.True);
        Assert.That(output, Is.EqualTo(original));
        output[0] = 0;
        Assert.That(key.CopyCanonicalState(), Is.EqualTo(original));
        Assert.Throws<InvalidOperationException>((Action)(() => _ = key.Components));
    }

    [Test]
    public void FromCanonical_VersionedAndLegacy_CompareWithoutConflation()
    {
        var bytes = Convert.FromHexString(Leaf);
        var first = ItemKey.FromCanonical("a", bytes);
        var second = ItemKey.FromCanonical("a", bytes.ToArray());
        var legacy = new ItemKey("a");
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.IdentityVersion, Is.EqualTo(1));
        Assert.That(legacy.IdentityVersion, Is.Zero);
        Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        Assert.That(first.CompareTo(second), Is.Zero);
        Assert.That(legacy, Is.Not.EqualTo(first));
        Assert.That(legacy.CompareTo(first), Is.LessThan(0));
        Assert.Throws<InvalidOperationException>((Action)(() => legacy.CopyCanonicalState()));
    }

    [Test]
    public void FromCanonical_InvalidFramingAndIdentity_Rejects()
    {
        var input = Convert.FromHexString(Leaf);
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("other", input)));
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", input[..^1])));
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", input.Concat(new byte[] { 0 }).ToArray())));
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", new byte[1048577])));
        var invalidVersion = input.ToArray(); invalidVersion[7] = 2;
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", invalidVersion)));
        var invalidUtf8 = input.ToArray(); invalidUtf8[15] = 0xff;
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", invalidUtf8)));
    }

    [Test]
    public void FromCanonical_NullCategoryAndWrongOwnerPath_Reject()
    {
        var input = Convert.FromHexString(Nested);
        var marker = Convert.FromHexString("0100000001660100000000");
        var index = Find(input, marker);
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        var nullCategory = input.Take(index + 6).Concat(new byte[] { 0 }).Concat(input.Skip(index + 11)).ToArray();
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("r", nullCategory)));
        var path = Convert.FromHexString("000000030103000000000400000000");
        var pathIndex = Find(input, path);
        Assert.That(pathIndex, Is.GreaterThanOrEqualTo(0));
        var wrongOwner = input.ToArray(); wrongOwner[pathIndex + path.Length - 1] = 1;
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("r", wrongOwner)));
    }

    [TestCase(6, 0, "", null, false)]
    [TestCase(7, 0, "", null, false)]
    [TestCase(0, 3, "", null, false)]
    [TestCase(0, 0, "contrabandMarkUp", null, false)]
    [TestCase(0, 0, "", "", false)]
    [TestCase(0, 0, "", "genuineInjector", false)]
    [TestCase(0, 0, "", null, true)]
    public void FromCanonical_FeatureAndConditionPolicy_IsEnforced(
        int type, int stage, string category, string? action, bool hasCondition)
    {
        var input = Envelope(Feature(type, stage, category, action, hasCondition || action != null));
        // Activation action IDs (for example "genuineInjector") are part of the identity and are accepted.
        if (type == 0 && stage == 0 && category == "")
        {
            Assert.That(ItemKey.FromCanonical("a", input).IsCanonical, Is.True);
        }
        else
        {
            Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", input)));
        }
    }

    [Test]
    public void FromCanonical_DuplicateFeatureIdentifiers_Rejects()
    {
        var feature = Feature(0, 0, "", null, false);
        var input = Envelope(features: new[] { feature, feature });
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", input)));
    }

    [Test]
    public void FromCanonical_DefaultTagAndTagOrdering_RejectNoncanonical()
    {
        var def = Tag("a", enabled: false, value: 0);
        var nondefault = Tag("a", enabled: true, value: 0);
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", Envelope(tags: new[] { def }))));
        Assert.That(ItemKey.FromCanonical("a", Envelope(tags: new[] { nondefault })).IsCanonical, Is.True);
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", Envelope(tags: new[] { Tag("b", true, 0), nondefault }))));
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", Envelope(tags: new[] { nondefault, nondefault }))));
        Assert.That(ItemKey.FromCanonical("a", Envelope(tags: new[] { Tag("a", false, -1) })).IsCanonical, Is.True);
    }

    [Test]
    public void FromCanonical_SignedZeroAndNonfinite_AreValidated()
    {
        var plus = Envelope(tags: new[] { Tag("a", true, 0, 0x00000000) });
        var minus = Envelope(tags: new[] { Tag("a", true, 0, 0x80000000) });
        Assert.That(ItemKey.FromCanonical("a", plus), Is.Not.EqualTo(ItemKey.FromCanonical("a", minus)));
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", Envelope(tags: new[] { Tag("a", true, 0, 0x7f800000) }))));
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", Envelope(tags: new[] { Tag("a", false, 0, 0x80000000) }))));
    }

    [Test]
    public void FromCanonical_AllTruncationsAndExcessiveCounts_Reject()
    {
        foreach (var hex in new[] { Leaf, Nested, MultiLevel })
        {
            var bytes = Convert.FromHexString(hex);
            var id = hex == Leaf ? "a" : "r";
            for (var length = 0; length < bytes.Length; length++)
            {
                var prefix = bytes.Take(length).ToArray();
                Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical(id, prefix)), $"length {length}");
            }
        }

        var badStringLength = Convert.FromHexString(Leaf);
        for (var i = 11; i < 15; i++) badStringLength[i] = 0xff;
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", badStringLength)));
    }

    [Test]
    public void FromCanonical_BadShapeMasksAndBounds_Reject()
    {
        var input = Convert.FromHexString(Nested);
        var shape = Convert.FromHexString("000000000000000000000001000000010000000000000000000100");
        var start = Find(input, shape);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var badMask = input.ToArray(); badMask[start + shape.Length - 1] = 2;
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("r", badMask)));
        var badOutside = input.ToArray(); badOutside[start + 21] = 2;
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("r", badOutside)));
        var badWidth = input.ToArray(); badWidth[start + 8] = 0x7f;
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("r", badWidth)));
    }

    [Test]
    public void FromCanonical_TagOrderingUsesOrdinalUtf16_NotUtf8()
    {
        var supplementary = "\ud800\udc00";
        var bmp = "\ue000";
        Assert.That(string.CompareOrdinal(supplementary, bmp), Is.LessThan(0));
        var first = Tag(supplementary, true, 0);
        var second = Tag(bmp, true, 0);
        Assert.That(ItemKey.FromCanonical("a", Envelope(tags: new[] { first, second })).IsCanonical, Is.True);
        Assert.Throws<ArgumentException>((Action)(() => ItemKey.FromCanonical("a", Envelope(tags: new[] { second, first }))));
    }

    private static int Find(byte[] source, byte[] value)
    {
        for (var i = 0; i <= source.Length - value.Length; i++)
        {
            if (source.AsSpan(i, value.Length).SequenceEqual(value)) return i;
        }
        return -1;
    }

    private static byte[] Envelope(byte[]? feature = null, byte[][]? tags = null, byte[][]? features = null)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("DSID"));
        U32(bytes, 1); bytes.Add(10); bytes.Add(1); String(bytes, "a");
        String(bytes, null); String(bytes, null); String(bytes, null);
        bytes.AddRange(new byte[32]); U32(bytes, 0); String(bytes, null); String(bytes, null);
        var items = tags ?? Array.Empty<byte[]>(); U32(bytes, items.Length);
        foreach (var tag in items) bytes.AddRange(tag);
        var featureItems = features ?? (feature == null ? Array.Empty<byte[]>() : new[] { feature });
        U32(bytes, featureItems.Length); foreach (var value in featureItems) bytes.AddRange(value);
        bytes.Add(0); bytes.Add(0);
        return bytes.ToArray();
    }

    private static byte[] Feature(int type, int stage, string category, string? action, bool condition)
    {
        var b = new List<byte>(); U32(b, type); U32(b, stage); U32(b, 0);
        String(b, null); String(b, null); String(b, null); U32(b, 0);
        b.AddRange(new byte[4]); b.Add((byte)(condition ? 1 : 0));
        if (condition)
        {
            String(b, null); String(b, null); U32(b, 0); U32(b, 0); b.Add(0);
            String(b, null); b.Add(0); U32(b, 0); String(b, action); U32(b, 0);
        }
        b.Add(0); b.Add(0); b.Add(0); U32(b, 0); b.Add(0); b.Add(0); U32(b, 0); b.Add(0);
        String(b, "feature"); String(b, category); b.Add(0);
        return b.ToArray();
    }

    private static byte[] Tag(string key, bool enabled, int value, uint floatBits = 0)
    {
        var b = new List<byte>(); String(b, key); String(b, key); String(b, null);
        b.Add((byte)(enabled ? 1 : 0)); String(b, null); U32(b, value); U32(b, unchecked((int)floatBits));
        b.AddRange(new byte[16]); b.Add(0); return b.ToArray();
    }

    private static void String(List<byte> b, string? value)
    {
        if (value == null) { b.Add(0); return; }
        var bytes = Encoding.UTF8.GetBytes(value); b.Add(1); U32(b, bytes.Length); b.AddRange(bytes);
    }

    private static void U32(List<byte> b, int value)
    {
        b.Add((byte)(value >> 24)); b.Add((byte)(value >> 16)); b.Add((byte)(value >> 8)); b.Add((byte)value);
    }
}
