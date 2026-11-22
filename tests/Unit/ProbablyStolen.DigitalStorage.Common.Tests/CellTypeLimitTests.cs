using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellTypeLimitTests
{
    private static readonly StorageCellTier Tier = StorageCellTier.All[0];

    private static IUpgradeInventory Modules(params InstalledModule[] modules) => new UpgradeInventory(modules);

    private static InstalledModule TypeLimit(ModuleQuality quality, int add, int percent) =>
        InstalledModule.TypeLimit(new TypeLimitRoll(quality, add, percent));

    [Test]
    public void Calculate_DefaultsToTierStorageAnd128Rules()
    {
        Assert.That(CellTypeLimit.Calculate(Tier, UpgradeInventory.Empty), Is.EqualTo((256, 128)));
        Assert.That(CellTypeLimit.Calculate(Tier, Modules(InstalledModule.EqualDistribution())), Is.EqualTo((256, 128)));
    }

    [Test]
    public void Calculate_AddsFlatBonusesThenSummedPercentages()
    {
        // Storage floors (256 + 640 + 100) * 1.4; rules floor (128 + 64 + 10) * 1.4.
        Assert.That(CellTypeLimit.Calculate(Tier, Modules(
            TypeLimit(ModuleQuality.Epic, 64, 30), TypeLimit(ModuleQuality.Common, 10, 10))), Is.EqualTo((1394, 282)));
        // 352.8 storage types and 142.8 rule slots both round down.
        Assert.That(CellTypeLimit.Calculate(Tier, Modules(TypeLimit(ModuleQuality.Common, 8, 5))), Is.EqualTo((352, 142)));
    }

    [Test]
    public void Calculate_HasNoUpperClampForFiveMaximalEpicModules()
    {
        var epic = TypeLimit(ModuleQuality.Epic, 64, 30);
        // (128 + 320) * 2.5 = 1120.
        Assert.That(CellTypeLimit.Calculate(Tier, Modules(epic, epic, epic, epic, epic)), Is.EqualTo((8640, 1120)));
    }

    [Test]
    public void Calculate_IgnoresInvalidModules()
    {
        Assert.That(CellTypeLimit.Calculate(Tier, Modules(
            InstalledModule.TypeLimit(null),
            TypeLimit(ModuleQuality.Common, 99, 5),
            TypeLimit(ModuleQuality.Common, 8, 5))), Is.EqualTo((352, 142)));
    }

    [Test]
    public void Calculate_BoundsResultsThatExceedInt32()
    {
        var epic = TypeLimit(ModuleQuality.Epic, 64, 30);
        var modules = new UpgradeInventory(Enumerable.Repeat(epic, 40000));
        Assert.That(CellTypeLimit.Calculate(Tier, modules), Is.EqualTo((int.MaxValue, int.MaxValue)));
    }

    [Test]
    public void Calculate_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(new Action(() => CellTypeLimit.Calculate(Tier, null!)));
        Assert.Throws<ArgumentNullException>(new Action(() => CellTypeLimit.Calculate(null!, UpgradeInventory.Empty)));
    }

    [Test]
    public void TierCapacity_ScalesOneToFourToTheTop()
    {
        long[] bytes = { 1024, 4096, 16384, 65536, 262144 };
        int[] types = { 256, 1024, 4096, 16384, 65536 };
        for (var index = 0; index < StorageCellTier.All.Count; index++)
        {
            var tier = StorageCellTier.All[index];
            Assert.That(tier.GetBytes(), Is.EqualTo(bytes[index]), tier.Name);
            Assert.That(tier.GetBytesPerType(), Is.EqualTo(2), tier.Name);
            Assert.That(tier.GetTotalTypes(), Is.EqualTo(types[index]), tier.Name);
        }
    }
}
