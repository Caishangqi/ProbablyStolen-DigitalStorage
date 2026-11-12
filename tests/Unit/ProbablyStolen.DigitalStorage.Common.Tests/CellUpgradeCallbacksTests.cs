using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellUpgradeCallbacksTests
{
    private static readonly StorageCellTier Tier1K = StorageCellTier.All[0];
    private static readonly InstalledModule Common8 = InstalledModule.TypeLimit(new TypeLimitRoll(ModuleQuality.Common, 8, 5));

    private static CellModuleState State(IUpgradeInventory? modules = null, StorageCellTier? tier = null,
        bool inWorkbench = true, bool available = true, bool restoring = false, int types = 0, long area = 0,
        bool housing = false) =>
        new(housing ? null : tier ?? Tier1K, modules ?? UpgradeInventory.Empty, inWorkbench, available, restoring, types, area);

    [Test]
    public void Install_RequiresAnAssembledAvailableCellInTheWorkbench()
    {
        Assert.That(CellUpgradeCallbacks.CanInstall(State(), CellModuleKind.TypeLimit).IsAllowed, Is.True);
        Assert.That(CellUpgradeCallbacks.CanInstall(State(housing: true), CellModuleKind.TypeLimit).Failure,
            Is.EqualTo(ModuleChangeFailure.HousingOnly));
        Assert.That(CellUpgradeCallbacks.CanInstall(State(inWorkbench: false), CellModuleKind.TypeLimit).Failure,
            Is.EqualTo(ModuleChangeFailure.NotInWorkbench));
        Assert.That(CellUpgradeCallbacks.CanInstall(State(available: false), CellModuleKind.TypeLimit).Failure,
            Is.EqualTo(ModuleChangeFailure.CellUnavailable));
    }

    [Test]
    public void Install_NeverBlocksASavedCellBeingRestored()
    {
        var full = UpgradeInventory.Empty.With(InstalledModule.EqualDistribution());
        Assert.That(CellUpgradeCallbacks.CanInstall(State(full, inWorkbench: false, available: false, restoring: true),
            CellModuleKind.EqualDistribution).IsAllowed, Is.True);
    }

    [Test]
    public void Install_AllowsOneEqualDistributionModuleAndManyTypeLimitModules()
    {
        var withEqual = UpgradeInventory.Empty.With(InstalledModule.EqualDistribution());
        Assert.That(CellUpgradeCallbacks.CanInstall(State(withEqual), CellModuleKind.EqualDistribution).Failure,
            Is.EqualTo(ModuleChangeFailure.LimitReached));

        var many = UpgradeInventory.Empty.With(Common8).With(Common8).With(Common8).With(Common8).With(Common8);
        Assert.That(CellUpgradeCallbacks.CanInstall(State(many), CellModuleKind.TypeLimit).IsAllowed, Is.True);
    }

    [Test]
    public void Remove_RequiresTheWorkbenchAndAnAvailableCell()
    {
        var modules = UpgradeInventory.Empty.With(InstalledModule.EqualDistribution());
        Assert.That(CellUpgradeCallbacks.CanRemove(State(modules), 0).IsAllowed, Is.True);
        Assert.That(CellUpgradeCallbacks.CanRemove(State(modules, inWorkbench: false), 0).Failure,
            Is.EqualTo(ModuleChangeFailure.NotInWorkbench));
        Assert.That(CellUpgradeCallbacks.CanRemove(State(modules, available: false), 0).Failure,
            Is.EqualTo(ModuleChangeFailure.CellUnavailable));
        Assert.That(CellUpgradeCallbacks.CanRemove(State(modules, restoring: true), 0).Failure,
            Is.EqualTo(ModuleChangeFailure.Restoring));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => CellUpgradeCallbacks.CanRemove(State(modules), 1)));
    }

    [Test]
    public void Remove_TypeLimitModuleWhenTheTypesStillFit()
    {
        // With the module the limit is (128 + 8) * 1.05 = 142; without it 128.
        var modules = UpgradeInventory.Empty.With(Common8);
        Assert.That(CellUpgradeCallbacks.CanRemove(State(modules, types: 128), 0).IsAllowed, Is.True);

        var check = CellUpgradeCallbacks.CanRemove(State(modules, types: 142), 0);
        Assert.That((check.Failure, check.Shortfall), Is.EqualTo((ModuleChangeFailure.TypesExceedLimit, 14L)));
    }

    [Test]
    public void Remove_TypeLimitModuleWhenTheSmallerLimitRaisesTheTypeCostPastCapacity()
    {
        // 128 types cost ceil(128 * 512 / 142) = 462 bytes now, but 512 once the limit falls to 128. With 4097 area units
        // (513 bytes) the cell uses 975 bytes now and would need 1025 > 1024.
        var modules = UpgradeInventory.Empty.With(Common8);
        Assert.That(CellUpgradeCallbacks.CanRemove(State(modules, types: 128, area: 4096), 0).IsAllowed, Is.True);

        var check = CellUpgradeCallbacks.CanRemove(State(modules, types: 128, area: 4097), 0);
        Assert.That((check.Failure, check.Shortfall), Is.EqualTo((ModuleChangeFailure.BytesExceedCapacity, 1L)));
    }

    [Test]
    public void Remove_InvalidTypeLimitModuleIsAlwaysAllowedBecauseItAddsNothing()
    {
        var modules = UpgradeInventory.Empty.With(InstalledModule.TypeLimit(null));
        Assert.That(CellUpgradeCallbacks.CanRemove(State(modules, types: 128, area: 4096), 0).IsAllowed, Is.True);
    }
}
