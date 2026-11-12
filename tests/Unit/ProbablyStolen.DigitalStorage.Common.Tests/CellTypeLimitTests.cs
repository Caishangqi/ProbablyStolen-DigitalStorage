using System;
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
    public void Effective_DefaultsTo128()
    {
        Assert.That(CellTypeLimit.Effective(Tier, UpgradeInventory.Empty), Is.EqualTo(128));
        Assert.That(CellTypeLimit.Effective(Tier, Modules(InstalledModule.EqualDistribution())), Is.EqualTo(128));
    }

    [Test]
    public void Effective_AddsFlatBonusesThenSummedPercentages()
    {
        // (128 + 64 + 10) * 1.4 = 282.8, rounded down to 282.
        Assert.That(CellTypeLimit.Effective(Tier, Modules(
            TypeLimit(ModuleQuality.Epic, 64, 30), TypeLimit(ModuleQuality.Common, 10, 10))), Is.EqualTo(282));
        // (128 + 8) * 1.05 = 142.8, rounded down to 142.
        Assert.That(CellTypeLimit.Effective(Tier, Modules(TypeLimit(ModuleQuality.Common, 8, 5))), Is.EqualTo(142));
    }

    [Test]
    public void Effective_HasNoUpperClampForFiveMaximalEpicModules()
    {
        var epic = TypeLimit(ModuleQuality.Epic, 64, 30);
        // (128 + 320) * 2.5 = 1120.
        Assert.That(CellTypeLimit.Effective(Tier, Modules(epic, epic, epic, epic, epic)), Is.EqualTo(1120));
    }

    [Test]
    public void Effective_IgnoresInvalidModules()
    {
        Assert.That(CellTypeLimit.Effective(Tier, Modules(
            InstalledModule.TypeLimit(null),
            TypeLimit(ModuleQuality.Common, 99, 5),
            TypeLimit(ModuleQuality.Common, 8, 5))), Is.EqualTo(142));
    }

    [Test]
    public void Effective_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(new Action(() => CellTypeLimit.Effective(Tier, null!)));
        Assert.Throws<ArgumentNullException>(new Action(() => CellTypeLimit.Effective(null!, UpgradeInventory.Empty)));
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
