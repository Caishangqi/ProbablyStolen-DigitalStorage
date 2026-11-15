using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Api.Core.Tests;

[TestFixture]
public sealed class CanonicalDecodeRoundTripTests
{
    [Test]
    public void Decode_LeafWithEveryRootField_ReencodesToSameBytes()
    {
        var input = new CanonicalIdentityInput("ingot", new[] { "MATERIAL", "MATERIAL_TAG" },
            new[]
            {
                new CanonicalIdentityTag("INGOT_PURITY_TAG", "id", "name", true, "INGOT_PURITY_FAIR", 3, 0x3f800000, -7,
                    0x3ff0000000000000, true),
                new CanonicalIdentityTag("A_TAG", valueInt: -1)
            },
            new[]
            {
                new CanonicalIdentityFeature("INGOT_PURITY_TAG", "purity", 2, 1, "Metal Purity: Fair", "actual", "tool",
                    15, true, true, false, true, new CanonicalIdentityCondition("fake", "c", 1, 2, true, "cat", true, 3, "act", 4),
                    new CanonicalIdentityCondition(), true, true, 9, true, false, 11, false, true)
            },
            "Metal Ingot", "short", "flavor", 1, 50, 2, 3, "Items/items_material", "metal_ingot");

        AssertRoundTrip(input);
    }

    [Test]
    public void Decode_ContentAndExamineWindows_ReencodesToSameBytes()
    {
        // Feature ids are unique across the whole identity, so only the first tablet carries one.
        var tablet = new CanonicalIdentityInput("water_tablet", Array.Empty<string>(), Array.Empty<CanonicalIdentityTag>(),
            new[] { new CanonicalIdentityFeature("chem", "") }, unitValue: 6);
        var plainTablet = new CanonicalIdentityInput("water_tablet", Array.Empty<string>(),
            Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>(), unitValue: 6);
        var mask = new byte[] { 1, 0, 1 };
        var grid = CanonicalIdentityNode.Grid(new CanonicalIdentityShape(0, 0, 1, 3, false, 0, 0, mask), new[]
        {
            new CanonicalIdentityChild(tablet, 1, Shape(), Shape()),
            new CanonicalIdentityChild(plainTablet, 2, Shape(), new CanonicalIdentityShape(2, 0, 1, 1, true, 1, 1, new byte[] { 1 }))
        });
        var slot = CanonicalIdentityNode.Slot(3, new CanonicalIdentityChild(plainTablet.Copy(), 1, Shape(), Shape()),
            new[] { CanonicalIdentityNode.Scrollable(4, 5, true, Array.Empty<CanonicalIdentityChild>()) });
        var input = new CanonicalIdentityInput("water_tablet_container", Array.Empty<string>(),
            Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>(), unitValue: 1,
            contentWindow: CanonicalIdentityNode.Window(new[] { grid }),
            examineWindow: CanonicalIdentityNode.Window(new[] { slot }));

        AssertRoundTrip(input);
    }

    [Test]
    public void Decode_ReturnsStructureThatMatchesTheInput()
    {
        var input = new CanonicalIdentityInput("box", new[] { "T" }, new[] { new CanonicalIdentityTag("K", enabled: true) },
            Array.Empty<CanonicalIdentityFeature>(), unitBaseValue: 4, lateUnitValue: 8,
            contentWindow: CanonicalIdentityNode.Window(new[]
            {
                CanonicalIdentityNode.Grid(Shape(), new[] { new CanonicalIdentityChild(Leaf("c"), 5, Shape(), Shape()) })
            }));
        var key = CanonicalIdentityEncoder.Encode(input);

        var decoded = CanonicalIdentityValidator.Decode(key.CopyCanonicalState(), "box");

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Identifier, Is.EqualTo("box"));
        Assert.That(decoded.ItemTypes, Is.EqualTo(new[] { "T" }));
        Assert.That(decoded.UnitBaseValue, Is.EqualTo(4));
        Assert.That(decoded.LateUnitValue, Is.EqualTo(8));
        Assert.That(decoded.Tags, Has.Count.EqualTo(1));
        var child = decoded.ContentWindow!.Nodes[0].Items[0];
        Assert.That(child.UnitCount, Is.EqualTo(5));
        Assert.That(child.Item.Identifier, Is.EqualTo("c"));
        Assert.That(decoded.ExamineWindow, Is.Null);
    }

    [Test]
    public void Decode_InvalidData_ReturnsNull()
    {
        var bytes = CanonicalIdentityEncoder.Encode(Leaf("a")).CopyCanonicalState();
        var truncated = bytes[..^1];
        var trailing = new List<byte>(bytes) { 0 }.ToArray();
        var badMagic = (byte[])bytes.Clone();
        badMagic[0] = (byte)'X';

        Assert.That(CanonicalIdentityValidator.Decode(truncated, "a"), Is.Null);
        Assert.That(CanonicalIdentityValidator.Decode(trailing, "a"), Is.Null);
        Assert.That(CanonicalIdentityValidator.Decode(badMagic, "a"), Is.Null);
        Assert.That(CanonicalIdentityValidator.Decode(bytes, "other"), Is.Null);
        Assert.That(CanonicalIdentityValidator.Decode(null!, "a"), Is.Null);
        Assert.That(CanonicalIdentityValidator.Decode(bytes, " "), Is.Null);
    }

    [Test]
    public void ValidationAndSummary_StillAcceptDecodableData()
    {
        var bytes = CanonicalIdentityEncoder.Encode(Leaf("a")).CopyCanonicalState();

        Assert.That(CanonicalIdentityValidator.IsValid(bytes, "a"), Is.True);
        Assert.That(CanonicalIdentityValidator.ReadSummary(bytes, "a")?.Identifier, Is.EqualTo("a"));
    }

    private static void AssertRoundTrip(CanonicalIdentityInput input)
    {
        var bytes = CanonicalIdentityEncoder.Encode(input).CopyCanonicalState();
        var decoded = CanonicalIdentityValidator.Decode(bytes, input.Identifier);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(CanonicalIdentityEncoder.Encode(decoded!).CopyCanonicalState(), Is.EqualTo(bytes));
    }

    private static CanonicalIdentityInput Leaf(string id) =>
        new(id, Array.Empty<string>(), Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>());

    private static CanonicalIdentityShape Shape() => new(0, 0, 1, 1, false, 0, 0, new byte[] { 0 });
}

internal static class CanonicalIdentityInputTestExtensions
{
    // The encoder rejects one object appearing twice, so repeated children need distinct instances.
    internal static CanonicalIdentityInput Copy(this CanonicalIdentityInput input) =>
        new(input.Identifier, input.ItemTypes, input.Tags, input.Features, input.Name, input.ShortDescription,
            input.FlavorText, input.UnitBaseValue, input.UnitValue, input.LateUnitValue, input.BackupUnitValue,
            input.SpriteAtlasPath, input.SpritePath, input.ContentWindow, input.ExamineWindow);
}
