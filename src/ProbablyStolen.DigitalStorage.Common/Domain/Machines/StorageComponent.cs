using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Machines;

/// <summary>Describes the component tier without allocating a digital inventory.</summary>
public sealed class StorageComponent
{
    /// <summary>The native directory identity of the supported component.</summary>
    public const string ItemId = "digital_storage.cell_component_1k";

    /// <summary>Tests the stable identity rather than localized display text.</summary>
    public bool IsStorageComponent(string? identifier) =>
        StorageCellTier.FromComponentId(identifier) != null;
}
