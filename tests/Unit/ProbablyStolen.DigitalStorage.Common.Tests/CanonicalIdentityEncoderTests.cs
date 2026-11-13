using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CanonicalIdentityEncoderTests
{
    private const string Leaf = "44534944000000010a01010000000161000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";
    private const string Nested = "44534944000000010a010100000001720000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001010000000102000000000000000000000001000000010000000000000000000100000000010b00000001000000000000000000000001000000010000000000000000000100000000000000000000000001000000010000000000000000000100010100000001630000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000100000000000000000000000301030000000004000000000000000000000000000000000000000000000000000000000000010000000166010000000000000000";
    private const string MultiLevel = "44534944000000010a01010000000172000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001010000000101000000010400000000010b0000000100000000000000000000000100000001000000000000000000010000000000000000000000000100000001000000000000000000010001010000000163000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000101000000010400000000010b0000000100000000000000000000000100000001000000000000000000010000000000000000000000000100000001000000000000000000010001010000000164000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000010000000000000000000000070203000000000300000000050103000000000500000000000000000000000000000000000000000000000000000100000001660100000000000000000000000000000000";

    [Test]
    public void Encode_ApprovedVectors_MatchesAndValidates()
    {
        var child = Child(Input("c", features: new[] { new CanonicalIdentityFeature("f", "") }));
        var nested = Input("r", content: CanonicalIdentityNode.Window(new[] { CanonicalIdentityNode.Grid(Shape(), new[] { child }) }));
        var inner = Child(Input("d", features: new[] { new CanonicalIdentityFeature("f", "") }));
        var middle = Child(Input("c", content: CanonicalIdentityNode.Window(new[] { CanonicalIdentityNode.Slot(0, inner, Array.Empty<CanonicalIdentityNode>()) })));
        var multi = Input("r", examine: CanonicalIdentityNode.Window(new[] { CanonicalIdentityNode.Window(new[] { CanonicalIdentityNode.Slot(0, middle, Array.Empty<CanonicalIdentityNode>()) }) }));
        Assert.That(CanonicalIdentityEncoder.Encode(Input("a")).CopyCanonicalState(), Is.EqualTo(Convert.FromHexString(Leaf)));
        Assert.That(CanonicalIdentityEncoder.Encode(nested).CopyCanonicalState(), Is.EqualTo(Convert.FromHexString(Nested)));
        Assert.That(CanonicalIdentityEncoder.Encode(multi).CopyCanonicalState(), Is.EqualTo(Convert.FromHexString(MultiLevel)));
    }

    [Test]
    public void Encode_DetachesInputCollectionsAndPreservesSource()
    {
        var mask = new byte[] { 0 };
        var shape = new CanonicalIdentityShape(0, 0, 1, 1, false, 0, 0, mask);
        var tags = new List<CanonicalIdentityTag> { new("b", enabled: true), new("a", valueInt: -1) };
        var children = new List<CanonicalIdentityChild> { Child(Input("c")) };
        var input = Input("r", tags: tags, content: CanonicalIdentityNode.Grid(shape, children));
        var first = CanonicalIdentityEncoder.Encode(input);
        Assert.That(tags.Select(t => t.Key), Is.EqualTo(new[] { "b", "a" }));
        mask[0] = 9; tags.Clear(); children.Clear(); shape.CopyMask()[0] = 1;
        Assert.That(CanonicalIdentityEncoder.Encode(input), Is.EqualTo(first));
        Assert.That(shape.CopyMask(), Is.EqualTo(new byte[] { 0 }));
    }

    [Test]
    public void Encode_DefaultTagsOmittedAndSignedZeroPreservedWhenPresent()
    {
        var empty = CanonicalIdentityEncoder.Encode(Input("a"));
        var omitted = CanonicalIdentityEncoder.Encode(Input("a", tags: new[] { new CanonicalIdentityTag("t", valueFloatBits: 0x80000000) }));
        var plus = CanonicalIdentityEncoder.Encode(Input("a", tags: new[] { new CanonicalIdentityTag("t", enabled: true) }));
        var minus = CanonicalIdentityEncoder.Encode(Input("a", tags: new[] { new CanonicalIdentityTag("t", enabled: true, valueFloatBits: 0x80000000) }));
        Assert.That(omitted, Is.EqualTo(empty));
        Assert.That(plus, Is.Not.EqualTo(minus));
        Assert.That(new ItemKey("a"), Is.Not.EqualTo(empty));
    }

    [Test]
    public void Encode_RejectsUnsupportedFeatureConditionAndBadValues()
    {
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", features: new[] { new CanonicalIdentityFeature("f", null!) }))));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", features: new[] { new CanonicalIdentityFeature("f", "contrabandMarkUp") }))));
        Assert.That(CanonicalIdentityEncoder.Encode(Input("a", features: new[] { new CanonicalIdentityFeature("f", "", fakeCondition: new CanonicalIdentityCondition(onActivateActionId: "counterfeitInjector")) })).IsCanonical, Is.True);
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", features: new[] { new CanonicalIdentityFeature("f", "", featureType: 6) }))));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", tags: new[] { new CanonicalIdentityTag("t", valueDoubleBits: 0x7ff0000000000000) }))));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", name: "\ud800"))));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", name: new string('x', 65537)))));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", content: CanonicalIdentityNode.Grid(new CanonicalIdentityShape(0, 0, 1, 1, false, 0, 2, new byte[] { 0 }), Array.Empty<CanonicalIdentityChild>())))));
    }

    [Test]
    public void Encode_ConditionAndNestedPlacementDifferencesRemainDistinct()
    {
        var feature = new CanonicalIdentityFeature("f", "", fakeCondition: new CanonicalIdentityCondition(display: "condition"));
        var root = Input("a", features: new[] { feature });
        Assert.That(CanonicalIdentityEncoder.Encode(root).IsCanonical, Is.True);
        var item = Input("child");
        var original = Shape();
        var first = new CanonicalIdentityChild(item, 1, original, Shape());
        var rotated = new CanonicalIdentityChild(item, 1, original, new CanonicalIdentityShape(0, 0, 1, 1, false, 1, 0, new byte[] { 0 }));
        var one = CanonicalIdentityEncoder.Encode(Input("root", content: CanonicalIdentityNode.Grid(Shape(), new[] { first })));
        var two = CanonicalIdentityEncoder.Encode(Input("root", content: CanonicalIdentityNode.Grid(Shape(), new[] { rotated })));
        Assert.That(one, Is.Not.EqualTo(two));
    }

    [Test]
    public void Encode_ContentAndPriceChangesProduceNewIdentityWhileIdentifierPartitionStillMatches()
    {
        const string itemId = "digital_storage.cell_component_1k";
        var oldKey = CanonicalIdentityEncoder.Encode(Input(itemId, name: "old name", shortDescription: "old", unitValue: 100));
        var renamedKey = CanonicalIdentityEncoder.Encode(Input(itemId, name: "new name", shortDescription: "new", unitValue: 100));
        var repricedKey = CanonicalIdentityEncoder.Encode(Input(itemId, name: "old name", shortDescription: "old", unitValue: 200));
        Assert.That(renamedKey, Is.Not.EqualTo(oldKey));
        Assert.That(repricedKey, Is.Not.EqualTo(oldKey));

        var rule = PartitionRule.ForSample("stable-rule", 0, oldKey);
        var config = new PartitionConfig(PartitionMode.Whitelist, new[] { rule });
        var partition = CellPartition.From(config, CanonicalAttributeSource.Instance);

        Assert.That(partition.Rejection(renamedKey), Is.Null);
        Assert.That(partition.Rejection(repricedKey), Is.Null);
        Assert.That(config.Rules[0].DisplayKey, Is.EqualTo(oldKey), "Synchronizing item content does not rewrite the persisted sample key.");
        Assert.That(config.Rules[0].Conditions[0].Target!.Text, Is.EqualTo(itemId));
    }

    [Test]
    public void Encode_RespectsStringAndNodeDepthLimits()
    {
        Assert.That(CanonicalIdentityEncoder.Encode(Input("a", name: new string('x', 65536))).IsCanonical, Is.True);
        var node = CanonicalIdentityNode.Window(Array.Empty<CanonicalIdentityNode>());
        for (var i = 1; i < 16; i++) node = CanonicalIdentityNode.Window(new[] { node });
        Assert.That(CanonicalIdentityEncoder.Encode(Input("a", content: node)).IsCanonical, Is.True);
        var tooDeep = CanonicalIdentityNode.Window(new[] { node });
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", content: tooDeep))));
    }

    [Test]
    public void Encode_EnvelopeBoundaryRejectsOverflow()
    {
        var types = Enumerable.Repeat(string.Empty, 200000).ToArray();
        var emptyTags = Array.Empty<CanonicalIdentityTag>();
        var emptyFeatures = Array.Empty<CanonicalIdentityFeature>();
        var baseline = new CanonicalIdentityInput("a", types, emptyTags, emptyFeatures, name: "");
        var nameLength = 1048576 - CanonicalIdentityEncoder.Encode(baseline).CopyCanonicalState().Length;
        var exact = new CanonicalIdentityInput("a", types, emptyTags, emptyFeatures, name: new string('x', nameLength));
        var overflow = new CanonicalIdentityInput("a", types, emptyTags, emptyFeatures, name: new string('x', nameLength + 1));
        Assert.That(CanonicalIdentityEncoder.Encode(exact).CopyCanonicalState().Length, Is.EqualTo(1048576));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(overflow)));
    }

    [Test]
    public void Input_ChangingCount_UsesCapturedBoundAndRejectsOversizedInitialCount()
    {
        var growing = new ChangingCountList<string>(1, int.MaxValue, "item");
        var input = new CanonicalIdentityInput("a", growing, Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>());
        Assert.That(growing.CountReads, Is.EqualTo(1));
        Assert.That(input.ItemTypes, Is.EqualTo(new[] { "item" }));
        Assert.That(CanonicalIdentityEncoder.Encode(input).IsCanonical, Is.True);

        var oversized = new ChangingCountList<string>(int.MaxValue, 1, "item");
        Assert.Throws<ArgumentException>((Action)(() => new CanonicalIdentityInput("a", oversized,
            Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>())));
        Assert.That(oversized.CountReads, Is.EqualTo(1));
        Assert.That(oversized.IndexReads, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Input_TooManyTags_RejectsBeforeCopying(bool enabled)
    {
        var tag = new CanonicalIdentityTag("tag", enabled: enabled);
        var tags = new ChangingCountList<CanonicalIdentityTag>(65537, 1, tag);
        Assert.Throws<ArgumentException>((Action)(() => Input("a", tags: tags)));
        Assert.That(tags.CountReads, Is.EqualTo(1));
        Assert.That(tags.IndexReads, Is.Zero);
    }

    [Test]
    public void Encode_OmittedTagText_UsesStrictLengthAndAggregateInputBounds()
    {
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", tags: new[]
        {
            new CanonicalIdentityTag(new string('x', 65537))
        }))));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", tags: new[]
        {
            new CanonicalIdentityTag("key", identifierName: "\ud800")
        }))));
        var longMetadata = new string('x', 65536);
        var omitted = Enumerable.Range(0, 17).Select(i => new CanonicalIdentityTag(i.ToString(), identifier: longMetadata)).ToArray();
        Assert.That(CanonicalIdentityEncoder.Encode(Input("a", tags: omitted)), Is.EqualTo(CanonicalIdentityEncoder.Encode(Input("a"))));
    }

    [Test]
    public void Encode_NondefaultTagText_UsesAggregateInputBound()
    {
        var longMetadata = new string('x', 65536);
        var tags = Enumerable.Range(0, 17).Select(i => new CanonicalIdentityTag(i.ToString(),
            identifier: longMetadata, enabled: true)).ToArray();
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", tags: tags))));
    }

    [Test]
    public void Encode_DefaultTagsCountAgainstAggregateGraphRecordLimit()
    {
        var tags = Enumerable.Range(0, 65536).Select(i => new CanonicalIdentityTag(i.ToString())).ToArray();
        Assert.That(CanonicalIdentityEncoder.Encode(Input("a", tags: tags)).IsCanonical, Is.True);
        var nested = Child(Input("child", tags: new[] { new CanonicalIdentityTag("extra") }));
        var root = Input("a", tags: tags, content: CanonicalIdentityNode.Grid(Shape(), new[] { nested }));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(root)));
    }

    [Test]
    public void Encode_SharedObjectsAndDuplicateFeatures_Rejects()
    {
        var tag = new CanonicalIdentityTag("a");
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", tags: new[] { tag, tag }))));
        var shared = Input("child");
        var node = CanonicalIdentityNode.Grid(Shape(), new[] { Child(shared), Child(shared) });
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("root", content: node))));
        var condition = new CanonicalIdentityCondition();
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("a", features: new[] { new CanonicalIdentityFeature("f", "", fakeCondition: condition, realCondition: condition) }))));
        var parentFeature = new CanonicalIdentityFeature("f", "");
        var child = Child(Input("child", features: new[] { new CanonicalIdentityFeature("f", "") }));
        Assert.Throws<ArgumentException>((Action)(() => CanonicalIdentityEncoder.Encode(Input("root", features: new[] { parentFeature }, content: CanonicalIdentityNode.Grid(Shape(), new[] { child })))));
    }

    private sealed class ChangingCountList<T> : IReadOnlyList<T>
    {
        private readonly int _first;
        private readonly int _subsequent;
        private readonly T _value;
        internal ChangingCountList(int first, int subsequent, T value)
        {
            _first = first; _subsequent = subsequent; _value = value;
        }
        internal int CountReads { get; private set; }
        internal int IndexReads { get; private set; }
        public int Count => ++CountReads == 1 ? _first : _subsequent;
        public T this[int index] { get { IndexReads++; return _value; } }
        public IEnumerator<T> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static CanonicalIdentityInput Input(string id, IReadOnlyList<CanonicalIdentityTag>? tags = null,
        IReadOnlyList<CanonicalIdentityFeature>? features = null, CanonicalIdentityNode? content = null,
        CanonicalIdentityNode? examine = null, string? name = null, string? shortDescription = null,
        string? flavorText = null, long unitValue = 0) => new(id, Array.Empty<string>(),
            tags ?? Array.Empty<CanonicalIdentityTag>(), features ?? Array.Empty<CanonicalIdentityFeature>(),
            name: name, shortDescription: shortDescription, flavorText: flavorText, unitValue: unitValue,
            contentWindow: content, examineWindow: examine);
    private static CanonicalIdentityShape Shape() => new(0, 0, 1, 1, false, 0, 0, new byte[] { 0 });
    private static CanonicalIdentityChild Child(CanonicalIdentityInput item) => new(item, 1, Shape(), Shape());
}
