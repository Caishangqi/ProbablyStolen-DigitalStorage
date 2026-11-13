using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class MigrationSourceTests
{
    [Test]
    public void MigrationSource_HasNoMod()
    {
        Assert.That(MigrationSource.Instance.ModId, Is.Null);
    }
}
