using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Machines;

/// <summary>Defines fixed cell bay limits without owning a second collection of physical items.</summary>
public sealed class DriveCellInventory
{
    private readonly StorageCellDisassemblyRecipe _recipe;

    /// <summary>Uses the assembly recipe as the authority for complete cell identity.</summary>
    public DriveCellInventory(StorageCellDisassemblyRecipe recipe)
    {
        _recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
    }

    /// <summary>Returns the number of independent bays.</summary>
    public int Size() => 14;

    /// <summary>Returns one for a valid bay and rejects an invalid index.</summary>
    public int GetSlotLimit(int slot)
    {
        if (slot < 0 || slot >= Size())
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return 1;
    }

    /// <summary>Accepts one complete cell with the required physical footprint.</summary>
    public bool IsItemValid(int slot, string? identifier, int count, int width, int height,
        string? componentId, int componentCount) =>
        slot >= 0 && slot < Size() && width == 1 && height == 2 &&
        _recipe.IsComplete(identifier, count, componentId, componentCount);
}
