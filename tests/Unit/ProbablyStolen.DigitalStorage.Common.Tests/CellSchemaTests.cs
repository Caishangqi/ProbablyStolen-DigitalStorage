using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellSchemaTests
{
    [Test]
    public void Classify_UsesTheCurrentSchemaOfThisBuild()
    {
        Assert.That(CellSchema.Current, Is.EqualTo(2));
        Assert.That(CellSchema.Classify(2), Is.EqualTo(SchemaGate.Current));
        Assert.That(CellSchema.Classify(CellSchema.Untagged), Is.EqualTo(SchemaGate.Migrate));
        Assert.That(CellSchema.Classify(3), Is.EqualTo(SchemaGate.TooNew));
    }

    [TestCase(5, 5, SchemaGate.Current)]
    [TestCase(4, 5, SchemaGate.Migrate)]
    [TestCase(3, 5, SchemaGate.TooOld)]
    [TestCase(1, 5, SchemaGate.TooOld)]
    [TestCase(6, 5, SchemaGate.TooNew)]
    [TestCase(int.MaxValue, 5, SchemaGate.TooNew)]
    [TestCase(0, 5, SchemaGate.Invalid)]
    [TestCase(-1, 5, SchemaGate.Invalid)]
    [TestCase(int.MinValue, 5, SchemaGate.Invalid)]
    public void Classify_SortsEverySchemaAgainstC(int schema, int current, SchemaGate expected)
    {
        Assert.That(CellSchema.Classify(schema, current), Is.EqualTo(expected));
    }

    [Test]
    public void Classify_LocksSchema1OnceTheCurrentSchemaIs3()
    {
        Assert.That(CellSchema.Classify(1, 3), Is.EqualTo(SchemaGate.TooOld));
        Assert.That(CellSchema.Classify(2, 3), Is.EqualTo(SchemaGate.Migrate));
    }

    [Test]
    public void Classify_RejectsANonPositiveCurrentSchema()
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => CellSchema.Classify(1, 0)));
    }

    [Test]
    public void UpgradePath_NamesAReleaseForEveryOlderSchema()
    {
        for (var schema = 1; schema < CellSchema.Current; schema++)
        {
            Assert.That(CellSchema.UpgradePath.TryGetValue(schema, out var version), Is.True, schema.ToString());
            Assert.That(version, Is.Not.Empty);
        }

        Assert.That(CellSchema.UpgradePath.ContainsKey(CellSchema.Current), Is.False);
    }
}
