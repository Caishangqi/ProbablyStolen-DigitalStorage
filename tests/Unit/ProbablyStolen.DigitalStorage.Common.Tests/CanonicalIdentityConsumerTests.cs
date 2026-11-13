using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CanonicalIdentityConsumerTests
{
    [Test]
    public void ContentAndPriceChangesProduceNewIdentityWhileIdentifierPartitionStillMatches()
    {
        const string itemId = "digital_storage.cell_component_1k";
        var oldKey = CanonicalIdentityEncoder.Encode(Input(itemId, "old name", "old", 100));
        var renamedKey = CanonicalIdentityEncoder.Encode(Input(itemId, "new name", "new", 100));
        var repricedKey = CanonicalIdentityEncoder.Encode(Input(itemId, "old name", "old", 200));
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

    private static CanonicalIdentityInput Input(string id, string name, string description, long unitValue) =>
        new(id, System.Array.Empty<string>(), System.Array.Empty<CanonicalIdentityTag>(),
            System.Array.Empty<CanonicalIdentityFeature>(), name: name, shortDescription: description, unitValue: unitValue);
}
