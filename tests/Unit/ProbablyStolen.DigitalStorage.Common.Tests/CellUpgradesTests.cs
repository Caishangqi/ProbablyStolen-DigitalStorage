using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellUpgradesTests
{
    private sealed class FixedRandom : IRandom
    {
        private readonly Queue<int> _values;
        internal FixedRandom(params int[] values) => _values = new Queue<int>(values);
        internal List<(int Min, int Max)> Calls { get; } = new();

        public int Next(int minInclusive, int maxExclusive)
        {
            Calls.Add((minInclusive, maxExclusive));
            var value = _values.Dequeue();
            Assert.That(value, Is.InRange(minInclusive, maxExclusive - 1), "The fixed value must lie in the requested range.");
            return value;
        }
    }

    [Test]
    public void Upgrades_LimitEqualDistributionToOneAndTypeLimitNotAtAll()
    {
        Assert.That(Upgrades.GetMaxInstallable(CellModuleKind.EqualDistribution), Is.EqualTo(1));
        Assert.That(Upgrades.GetMaxInstallable(CellModuleKind.TypeLimit), Is.EqualTo(int.MaxValue));
        Assert.That(Upgrades.GetMaxInstallable((CellModuleKind)99), Is.Zero);
    }

    [Test]
    public void UpgradeInventory_CountsKindsAndSupportsAddAndRemove()
    {
        var roll = new TypeLimitRoll(ModuleQuality.Common, 10, 5);
        var inventory = UpgradeInventory.Empty
            .With(InstalledModule.TypeLimit(roll))
            .With(InstalledModule.EqualDistribution())
            .With(InstalledModule.TypeLimit(null));

        Assert.That(inventory.GetInstalledUpgrades(CellModuleKind.TypeLimit), Is.EqualTo(2));
        Assert.That(((IUpgradeInventory)inventory).IsInstalled(CellModuleKind.EqualDistribution), Is.True);
        var without = inventory.Without(1);
        Assert.That(((IUpgradeInventory)without).IsInstalled(CellModuleKind.EqualDistribution), Is.False);
        Assert.That(inventory.Modules.Count, Is.EqualTo(3), "Views are immutable.");
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => inventory.Without(3)));
        Assert.Throws<ArgumentException>(new Action(() => _ = new UpgradeInventory(new InstalledModule[] { null! })));
    }

    [Test]
    public void InstalledModule_IsInvalidWhenTheRollIsMissingOrOutOfRange()
    {
        Assert.That(InstalledModule.EqualDistribution().IsValid, Is.True);
        Assert.That(InstalledModule.TypeLimit(new TypeLimitRoll(ModuleQuality.Rare, 16, 20)).IsValid, Is.True);
        Assert.That(InstalledModule.TypeLimit(null).IsValid, Is.False);
        Assert.That(InstalledModule.TypeLimit(new TypeLimitRoll(ModuleQuality.Rare, 15, 20)).IsValid, Is.False);
        Assert.That(InstalledModule.TypeLimit(new TypeLimitRoll(ModuleQuality.Rare, 16, 21)).IsValid, Is.False);
        Assert.That(InstalledModule.TypeLimit(new TypeLimitRoll((ModuleQuality)7, 16, 20)).IsValid, Is.False);
    }

    [TestCase(ModuleQuality.Common, 8, 16, 5, 10)]
    [TestCase(ModuleQuality.Rare, 16, 32, 10, 20)]
    [TestCase(ModuleQuality.Epic, 32, 64, 20, 30)]
    public void Roll_DrawsEachValueWithinItsInclusiveRange(ModuleQuality quality, int addMin, int addMax,
        int percentMin, int percentMax)
    {
        var random = new FixedRandom(addMax, percentMin);
        var roll = TypeLimitRolls.Roll(quality, random);

        Assert.That((roll.Quality, roll.Add, roll.Percent), Is.EqualTo((quality, addMax, percentMin)));
        Assert.That(random.Calls, Is.EqualTo(new[] { (addMin, addMax + 1), (percentMin, percentMax + 1) }));
        Assert.That(TypeLimitRolls.IsValid(roll), Is.True);
    }

    [TestCase(0, ModuleQuality.Common)]
    [TestCase(59, ModuleQuality.Common)]
    [TestCase(60, ModuleQuality.Rare)]
    [TestCase(89, ModuleQuality.Rare)]
    [TestCase(90, ModuleQuality.Epic)]
    [TestCase(99, ModuleQuality.Epic)]
    public void RollQuality_UsesTheSixtyThirtyTenWeights(int draw, ModuleQuality expected)
    {
        var random = new FixedRandom(draw);

        Assert.That(TypeLimitRolls.RollQuality(random), Is.EqualTo(expected));
        Assert.That(random.Calls.Single(), Is.EqualTo((0, 100)));
    }

    [Test]
    public void RollQuality_HonoursCommonOnlyWeightsAndRejectsInvalidWeights()
    {
        Assert.That(TypeLimitRolls.RollQuality(new FixedRandom(0), 1, 0, 0), Is.EqualTo(ModuleQuality.Common));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => TypeLimitRolls.RollQuality(new FixedRandom(), 0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => TypeLimitRolls.RollQuality(new FixedRandom(), -1, 1, 1)));
    }

    [TestCase(ModuleQuality.Common, 8, 5, 80)]
    [TestCase(ModuleQuality.Common, 16, 10, 120)]
    [TestCase(ModuleQuality.Common, 12, 5, 90)]
    [TestCase(ModuleQuality.Rare, 32, 20, 240)]
    [TestCase(ModuleQuality.Epic, 32, 20, 320)]
    [TestCase(ModuleQuality.Epic, 64, 30, 480)]
    [TestCase(ModuleQuality.Epic, 48, 25, 400)]
    public void Price_ScalesFromPointEightToOnePointTwoOfTheBase(ModuleQuality quality, int add, int percent, long expected)
    {
        Assert.That(TypeLimitRolls.Price(new TypeLimitRoll(quality, add, percent)), Is.EqualTo(expected));
    }

    [Test]
    public void Price_RoundsHalfAwayFromZero()
    {
        // Common, add 9 (1/8) and percent 5 (0): p = 1/16, yielding 82.5 before rounding.
        Assert.That(TypeLimitRolls.Price(new TypeLimitRoll(ModuleQuality.Common, 9, 5)), Is.EqualTo(83));
        Assert.Throws<ArgumentException>(new Action(() => TypeLimitRolls.Price(new TypeLimitRoll(ModuleQuality.Common, 7, 5))));
    }
}
