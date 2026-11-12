using System;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>Coordinates native slot callbacks. Native inventories remain the sole owners of physical items.</summary>
public sealed class CellAssemblyCallbacks
{
    private readonly StorageCellDisassemblyRecipe _recipe;
    private readonly ICellAssemblyPort _port;
    private bool _validRemoval;

    /// <summary>Binds one host graph and its shared identity rules.</summary>
    public CellAssemblyCallbacks(StorageCellDisassemblyRecipe recipe, ICellAssemblyPort port)
    {
        _recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        _port = port ?? throw new ArgumentNullException(nameof(port));
    }

    /// <summary>Checks admission without moving, fixing, or renaming anything.</summary>
    public bool CanInsert(PhysicalItemSnapshot candidate, bool isCurrent, bool ownershipAllowed)
    {
        var state = _port.Read();
        if (isCurrent)
        {
            return IsComplete(state);
        }

        return ownershipAllowed && _recipe.CanAssemble(state.Host.Identifier, state.Host.Count,
            candidate.Identifier, candidate.Count, candidate.Width, candidate.Height,
            state.Component != null, state.Mounted) && !StageExceedsTier(candidate);
    }

    /// <summary>
    /// Gets whether the housing's module bay was expanded beyond what the candidate component's tier allows, so the
    /// component may not be assembled into it.
    /// </summary>
    public bool StageExceedsTier(PhysicalItemSnapshot candidate)
    {
        var tier = StorageCellTier.FromComponentId(candidate.Identifier);
        return tier != null && !ModuleBayExpansion.Fits(_port.Read().ExpansionStage, tier);
    }

    /// <summary>Checks removal and exposes a stable reason for a rejected extraction.</summary>
    public bool CanRemove() => GetRemovalFailure() == CellRemovalFailure.NONE;

    /// <summary>Gets the reason a component cannot currently be extracted.</summary>
    public CellRemovalFailure GetRemovalFailure()
    {
        var state = _port.Read();
        var child = state.Component;
        if (state.Restoring)
        {
            return CellRemovalFailure.RESTORING;
        }

        if (child == null)
        {
            return CellRemovalFailure.MISSING_COMPONENT;
        }

        if (!_recipe.IsComplete(state.Host.Identifier, state.Host.Count,
            child.Identifier, child.Count))
        {
            return CellRemovalFailure.INVALID_ASSEMBLY;
        }

        if (child.FixedInPlace)
        {
            return CellRemovalFailure.STUCK;
        }

        if (state.Mounted)
        {
            return CellRemovalFailure.MOUNTED;
        }

        return state.StorageState switch
        {
            StorageContentsState.Empty when state.HasModules => CellRemovalFailure.HAS_MODULES,
            StorageContentsState.Empty when state.HasPartitionRules => CellRemovalFailure.HAS_PARTITION_RULES,
            StorageContentsState.Empty => CellRemovalFailure.NONE,
            StorageContentsState.NonEmpty => CellRemovalFailure.STORAGE_CELL_NOT_EMPTY,
            _ => CellRemovalFailure.STORAGE_STATE_UNAVAILABLE
        };
    }

    /// <summary>Handles a committed insertion. Restored children keep their saved fixed state.</summary>
    public bool Inserted()
    {
        var state = _port.Read();
        if (state.Restoring)
        {
            return true;
        }

        var child = state.Component;
        if (child == null)
        {
            return false;
        }

        var assembly = _recipe.CanAssemble(state.Host.Identifier, state.Host.Count,
            child.Identifier, child.Count, child.Width, child.Height, false, state.Mounted);
        if (!assembly && !IsComplete(state))
        {
            return false;
        }

        _port.FixComponent();
        _port.Present(true);
        return true;
    }

    /// <summary>Captures legality before the original component leaves its native slot.</summary>
    public void Removing(bool isCurrent)
    {
        _validRemoval = isCurrent && GetRemovalFailure() == CellRemovalFailure.NONE;
    }

    /// <summary>Changes the host only after a legal removal has actually emptied its slot.</summary>
    public void Removed()
    {
        var valid = _validRemoval;
        _validRemoval = false;
        var state = _port.Read();
        if (valid && !state.Restoring && state.Component == null)
        {
            _port.Present(false);
        }
    }

    /// <summary>Detects an incomplete graph without repairing, discarding, or replacing it.</summary>
    public bool IsConsistent()
    {
        var state = _port.Read();
        return state.Restoring || IsComplete(state) ||
            _recipe.IsHousing(state.Host.Identifier, state.Host.Count, state.Component != null);
    }

    private bool IsComplete(CellAssemblySnapshot state) =>
        _recipe.IsComplete(state.Host.Identifier, state.Host.Count,
            state.Component?.Identifier, state.Component?.Count ?? 0);
}
