namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>Explains why a storage component cannot be extracted.</summary>
public enum CellRemovalFailure
{
    /// <summary>The component can be extracted.</summary>
    NONE,

    /// <summary>The component is missing or not the current slot child.</summary>
    MISSING_COMPONENT,

    /// <summary>The cell graph is not a complete supported assembly.</summary>
    INVALID_ASSEMBLY,

    /// <summary>The component is locked in place.</summary>
    STUCK,

    /// <summary>The cell is mounted in a drive.</summary>
    MOUNTED,

    /// <summary>The digital storage component contains items and must be emptied first.</summary>
    STORAGE_CELL_NOT_EMPTY,

    /// <summary>The native save decoder is still restoring the graph.</summary>
    RESTORING,

    /// <summary>Stored contents are unavailable or require recovery.</summary>
    STORAGE_STATE_UNAVAILABLE
}
