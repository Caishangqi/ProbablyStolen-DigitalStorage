using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>Explains why a module cannot be installed in or removed from a cell.</summary>
public enum ModuleChangeFailure
{
    /// <summary>The change is allowed.</summary>
    None = 0,

    /// <summary>Modules are changed only while the cell sits in a cell workbench.</summary>
    NotInWorkbench = 1,

    /// <summary>The cell already holds the most modules of this kind.</summary>
    LimitReached = 2,

    /// <summary>Removing the module would leave more stored types than the new limit; <c>Shortfall</c> types too many.</summary>
    TypesExceedLimit = 3,

    /// <summary>Removing the module would raise the type cost past the capacity; <c>Shortfall</c> bytes too many.</summary>
    BytesExceedCapacity = 4,

    /// <summary>The cell is unavailable or its pages are inconsistent, so its modules must not change.</summary>
    CellUnavailable = 5,

    /// <summary>A housing without a component accepts no modules.</summary>
    HousingOnly = 6,

    /// <summary>The native save decoder is still restoring the cell.</summary>
    Restoring = 7
}

/// <summary>The outcome of a module change check.</summary>
public readonly struct ModuleChangeCheck
{
    private ModuleChangeCheck(ModuleChangeFailure failure, long shortfall)
    {
        Failure = failure;
        Shortfall = shortfall;
    }

    /// <summary>Gets an allowed result.</summary>
    public static ModuleChangeCheck Allowed => default;

    /// <summary>Gets the failure, or <see cref="ModuleChangeFailure.None"/>.</summary>
    public ModuleChangeFailure Failure { get; }

    /// <summary>Gets how many types or bytes must be cleared first; 0 for other failures.</summary>
    public long Shortfall { get; }

    /// <summary>Gets whether the change is allowed.</summary>
    public bool IsAllowed => Failure == ModuleChangeFailure.None;

    internal static ModuleChangeCheck Fail(ModuleChangeFailure failure, long shortfall = 0) => new(failure, shortfall);
}

/// <summary>What a module change check reads about a cell.</summary>
public sealed class CellModuleState
{
    /// <summary>Creates a snapshot; <paramref name="tier"/> is null for a housing without a component.</summary>
    public CellModuleState(StorageCellTier? tier, IUpgradeInventory modules, bool inWorkbench, bool available,
        bool restoring, int storedTypes, long storedAreaUnits)
    {
        if (storedTypes < 0) throw new ArgumentOutOfRangeException(nameof(storedTypes));
        if (storedAreaUnits < 0) throw new ArgumentOutOfRangeException(nameof(storedAreaUnits));
        Tier = tier;
        Modules = modules ?? throw new ArgumentNullException(nameof(modules));
        InWorkbench = inWorkbench;
        Available = available;
        Restoring = restoring;
        StoredTypes = storedTypes;
        StoredAreaUnits = storedAreaUnits;
    }

    /// <summary>Gets the component tier, or null for a housing.</summary>
    public StorageCellTier? Tier { get; }

    /// <summary>Gets the modules installed now.</summary>
    public IUpgradeInventory Modules { get; }

    /// <summary>Gets whether the cell sits in a cell workbench.</summary>
    public bool InWorkbench { get; }

    /// <summary>Gets whether the cell's storage is readable and its pages match their layout.</summary>
    public bool Available { get; }

    /// <summary>Gets whether the native save decoder is restoring the cell.</summary>
    public bool Restoring { get; }

    /// <summary>Gets the stored type count.</summary>
    public int StoredTypes { get; }

    /// <summary>Gets the stored area units.</summary>
    public long StoredAreaUnits { get; }
}

/// <summary>Decides module installation and removal; the native module bay asks it before moving an item.</summary>
public static class CellUpgradeCallbacks
{
    /// <summary>Checks installing one more module.</summary>
    /// <remarks>Restoring a saved cell puts its modules back without a check, so restoring never blocks installation.</remarks>
    public static ModuleChangeCheck CanInstall(CellModuleState cell, CellModuleKind kind)
    {
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        if (cell.Restoring) return ModuleChangeCheck.Allowed;
        if (cell.Tier == null) return ModuleChangeCheck.Fail(ModuleChangeFailure.HousingOnly);
        if (!cell.InWorkbench) return ModuleChangeCheck.Fail(ModuleChangeFailure.NotInWorkbench);
        if (!cell.Available) return ModuleChangeCheck.Fail(ModuleChangeFailure.CellUnavailable);
        return cell.Modules.GetInstalledUpgrades(kind) >= Upgrades.GetMaxInstallable(kind)
            ? ModuleChangeCheck.Fail(ModuleChangeFailure.LimitReached)
            : ModuleChangeCheck.Allowed;
    }

    /// <summary>Checks removing the module at a bay index.</summary>
    /// <remarks>
    /// A type limit module may leave only when the stored types fit the limit recomputed without it, and the stored
    /// types and area still fit the capacity once the type cost is spread over that smaller limit.
    /// </remarks>
    public static ModuleChangeCheck CanRemove(CellModuleState cell, int index)
    {
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        if (index < 0 || index >= cell.Modules.Modules.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "No module at this index.");
        if (cell.Restoring) return ModuleChangeCheck.Fail(ModuleChangeFailure.Restoring);
        if (!cell.InWorkbench) return ModuleChangeCheck.Fail(ModuleChangeFailure.NotInWorkbench);
        if (!cell.Available) return ModuleChangeCheck.Fail(ModuleChangeFailure.CellUnavailable);

        var module = cell.Modules.Modules[index];
        if (cell.Tier == null || module.Kind != CellModuleKind.TypeLimit || !module.IsValid) return ModuleChangeCheck.Allowed;

        var after = new UpgradeInventory(cell.Modules.Modules).Without(index);
        var limit = CellTypeLimit.Effective(cell.Tier, after);
        if (cell.StoredTypes > limit)
            return ModuleChangeCheck.Fail(ModuleChangeFailure.TypesExceedLimit, cell.StoredTypes - limit);

        var used = CellView.UsedBytes(cell.StoredTypes, cell.StoredAreaUnits, cell.Tier.GetTypeBudget(), limit);
        var over = checked(used - cell.Tier.GetBytes());
        return over > 0 ? ModuleChangeCheck.Fail(ModuleChangeFailure.BytesExceedCapacity, over) : ModuleChangeCheck.Allowed;
    }
}
