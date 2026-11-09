namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Describes the state shown by a storage cell.</summary>
public enum CellState
{
    /// <summary>No cell is available.</summary>
    Absent = 0,

    /// <summary>The cell has no stored items.</summary>
    Empty = 1,

    /// <summary>The cell has stored items and free capacity.</summary>
    NotEmpty = 2,

    /// <summary>The cell has reached its type limit.</summary>
    TypesFull = 3,

    /// <summary>The cell has no remaining capacity.</summary>
    Full = 4
}
