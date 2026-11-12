using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Recipes;

/// <summary>Describes assembly and removal of an existing component and housing.</summary>
public sealed class StorageCellDisassemblyRecipe
{
    /// <summary>The empty housing identity.</summary>
    public const string HousingId = "digital_storage.item_cell_housing";
    /// <summary>The assembled cell identity.</summary>
    public const string CellId = "digital_storage.item_storage_cell_1k";
    private readonly StorageComponent _component;

    /// <summary>Uses the supported component definition for assembly validation.</summary>
    public StorageCellDisassemblyRecipe(StorageComponent component)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
    }

    /// <summary>Allows one correctly sized component into an empty, unmounted housing.</summary>
    public bool CanAssemble(string? housingId, int housingCount, string? componentId,
        int componentCount, int width, int height, bool occupied, bool mounted) =>
        string.Equals(housingId, HousingId, StringComparison.Ordinal) && housingCount == 1 &&
        _component.IsStorageComponent(componentId) && componentCount == 1 &&
        width == 1 && height == 2 && !occupied && !mounted;

    /// <summary>Requires exactly one original component inside one assembled cell.</summary>
    public bool IsComplete(string? cellId, int cellCount, string? componentId, int componentCount) =>
        cellCount == 1 && componentCount == 1 && _component.IsStorageComponent(componentId) &&
        StorageCellTier.FromCellId(cellId) is { } tier && tier == StorageCellTier.FromComponentId(componentId);

    /// <summary>Recognizes an empty housing without changing inconsistent objects.</summary>
    public bool IsHousing(string? identifier, int count, bool occupied) =>
        string.Equals(identifier, HousingId, StringComparison.Ordinal) && count == 1 && !occupied;

    /// <summary>Requires a complete cell, an unlocked component, and no drive mounting.</summary>
    public bool CanDisassemble(string? cellId, int cellCount, string? componentId, int componentCount,
        bool stuck, bool mounted) => CanDisassemble(cellId, cellCount, componentId, componentCount,
            stuck, mounted, false);

    /// <summary>Also requires an empty digital storage component, an empty module bay and no partition rule.</summary>
    public bool CanDisassemble(string? cellId, int cellCount, string? componentId, int componentCount,
        bool stuck, bool mounted, bool storageEmpty, bool hasModules = false, bool hasRules = false) =>
        IsComplete(cellId, cellCount, componentId, componentCount) && !stuck && !mounted && storageEmpty &&
        !hasModules && !hasRules;

}
