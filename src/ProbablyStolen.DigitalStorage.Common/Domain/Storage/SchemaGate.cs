namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>What the running mod may do with a cell of a given storage schema.</summary>
public enum SchemaGate
{
    /// <summary>The cell uses the current schema.</summary>
    Current = 0,

    /// <summary>The cell uses the previous schema and is migrated after load.</summary>
    Migrate = 1,

    /// <summary>The cell is two or more schemas behind; it is locked until upgraded step by step.</summary>
    TooOld = 2,

    /// <summary>The cell was written by a newer mod; it is unavailable.</summary>
    TooNew = 3,

    /// <summary>The recorded schema is not a positive number; the cell is unavailable.</summary>
    Invalid = 4
}
