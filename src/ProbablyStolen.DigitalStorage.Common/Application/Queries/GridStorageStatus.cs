namespace ProbablyStolen.DigitalStorage.Common.Application.Queries;

/// <summary>Explains whether a current grid can expose its cell storage.</summary>
public enum GridStorageStatus
{
    /// <summary>The grid has no controller.</summary>
    NoController = 0,

    /// <summary>The grid has more than one controller.</summary>
    ControllerConflict = 1,

    /// <summary>The controller is structurally valid but currently unpowered.</summary>
    Unpowered = 2,

    /// <summary>At least one mounted cell cannot expose its stored contents safely.</summary>
    StorageUnavailable = 3,

    /// <summary>The grid is available for storage operations.</summary>
    Online = 4
}
