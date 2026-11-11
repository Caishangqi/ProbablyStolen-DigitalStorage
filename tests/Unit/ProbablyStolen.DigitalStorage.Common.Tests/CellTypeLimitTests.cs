using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellTypeLimitTests
{
    private static readonly StorageCellTier Tier = StorageCellTier.All[0];

    [Test]
    public void Effective_DefaultsTo128()
    {
        Assert.That(CellTypeLimit.Effective(Tier, Array.Empty<TypeLimitExtension>()), Is.EqualTo(128));
    }

    [Test]
    public void Effective_AddsExtensionsAndClampsTo192()
    {
        Assert.That(CellTypeLimit.Effective(Tier, new[]
        {
            new TypeLimitExtension("a", 10), new TypeLimitExtension("b", 0), new TypeLimitExtension("c", 20)
        }), Is.EqualTo(158));
        Assert.That(CellTypeLimit.Effective(Tier, new[] { new TypeLimitExtension("a", 64) }), Is.EqualTo(192));
        Assert.That(CellTypeLimit.Effective(Tier, new[]
        {
            new TypeLimitExtension("a", int.MaxValue), new TypeLimitExtension("b", int.MaxValue)
        }), Is.EqualTo(192));
    }

    [Test]
    public void Effective_RejectsInvalidExtensions()
    {
        Assert.Throws<ArgumentException>(new Action(() =>
            CellTypeLimit.Effective(Tier, new[] { new TypeLimitExtension("a", -1) })));
        Assert.Throws<ArgumentException>(new Action(() =>
            CellTypeLimit.Effective(Tier, new[] { new TypeLimitExtension(null!, 1) })));
        Assert.Throws<ArgumentException>(new Action(() =>
            CellTypeLimit.Effective(Tier, new[] { new TypeLimitExtension("a", 1), new TypeLimitExtension("a", 2) })));
        Assert.Throws<ArgumentNullException>(new Action(() => CellTypeLimit.Effective(Tier, null!)));
        Assert.Throws<ArgumentNullException>(new Action(() =>
            CellTypeLimit.Effective(null!, Array.Empty<TypeLimitExtension>())));
    }

    [Test]
    public void TierCapacity_ScalesOneToFourToTheTop()
    {
        long[] bytes = { 1024, 4096, 16384, 65536, 262144 };
        long[] perType = { 4, 16, 64, 256, 1024 };
        for (var index = 0; index < StorageCellTier.All.Count; index++)
        {
            var tier = StorageCellTier.All[index];
            Assert.That(tier.GetBytes(), Is.EqualTo(bytes[index]), tier.Name);
            Assert.That(tier.GetBytesPerType(), Is.EqualTo(perType[index]), tier.Name);
            Assert.That(tier.GetTotalTypes(), Is.EqualTo(128), tier.Name);
        }
    }
}
