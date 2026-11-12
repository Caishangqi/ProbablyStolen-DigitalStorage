using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

/// <summary>
/// Attribute rules replace the exact identity partition; none of the removed types may
/// return alongside the new model.
/// </summary>
[TestFixture]
public sealed class RemovedPartitionTypesTests
{
    [TestCase("PrecisePriorityList")]
    [TestCase("IncludeExclude")]
    [TestCase("TypeLimitExtension")]
    public void ReplacedTypeIsGone(string name)
    {
        var types = typeof(CellPartition).Assembly.GetTypes().Select(type => type.Name);

        Assert.That(types, Does.Not.Contain(name));
    }
}
