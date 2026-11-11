namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>What a cell may do this session.</summary>
public enum CellHealthStatus
{
    /// <summary>The cell stores and releases items.</summary>
    Available = 0,

    /// <summary>Migration was refused; the cell only releases items.</summary>
    ReadOnly = 1,

    /// <summary>Removing empty pages failed and was rolled back; the cell stays usable.</summary>
    CompactionBlocked = 2,

    /// <summary>The schema is too old to migrate; pages are restored and safe to save, but the cell is not used.</summary>
    Locked = 3,

    /// <summary>The cell cannot be used; see the reason.</summary>
    Unavailable = 4
}

/// <summary>Why a cell is unavailable.</summary>
public enum CellUnavailableReason
{
    /// <summary>The cell is not unavailable.</summary>
    None = 0,

    /// <summary>Native decoding did not rebuild every recorded page.</summary>
    RestoreFailed = 1,

    /// <summary>The layout or page count tag cannot be read.</summary>
    InvalidLayout = 2,

    /// <summary>A newer mod wrote the cell, or the schema tag is not a positive number.</summary>
    SchemaTooNew = 3,

    /// <summary>Migration stopped partway.</summary>
    MigrationFailed = 4,

    /// <summary>Removing empty pages failed and so did its rollback.</summary>
    RollbackFailed = 5,

    /// <summary>A type limit extension is invalid.</summary>
    InvalidTypeLimit = 6
}

/// <summary>The session state of one cell.</summary>
public readonly struct CellHealthState
{
    /// <summary>Creates a state.</summary>
    public CellHealthState(CellHealthStatus status, CellUnavailableReason reason = CellUnavailableReason.None)
    {
        Status = status;
        Reason = status == CellHealthStatus.Unavailable ? reason : CellUnavailableReason.None;
    }

    /// <summary>Gets an available state.</summary>
    public static CellHealthState Available => new(CellHealthStatus.Available);

    /// <summary>Gets the status.</summary>
    public CellHealthStatus Status { get; }

    /// <summary>Gets the reason of an unavailable state.</summary>
    public CellUnavailableReason Reason { get; }

    /// <summary>Gets whether saving now could lose items, so the player must be told not to save.</summary>
    /// <remarks>Locked and pending cells keep their pages and are safe to save; they are never warned about.</remarks>
    public bool RisksItemLoss => Status == CellHealthStatus.Unavailable && Reason != CellUnavailableReason.InvalidTypeLimit;

    /// <summary>Gets whether the cell may be mounted in a network.</summary>
    public bool IsMountable => Status is CellHealthStatus.Available or CellHealthStatus.ReadOnly or CellHealthStatus.CompactionBlocked;

    /// <summary>Assesses a cell right after a save loaded, before migration runs.</summary>
    /// <param name="gate">The schema classification.</param>
    /// <param name="layoutValid">Whether the layout could be read.</param>
    /// <param name="pagesComplete">Whether the native pages match the layout.</param>
    public static CellHealthState AfterLoad(SchemaGate gate, bool layoutValid, bool pagesComplete)
    {
        if (gate is SchemaGate.TooNew or SchemaGate.Invalid)
            return new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.SchemaTooNew);
        if (!layoutValid) return new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.InvalidLayout);
        if (!pagesComplete) return new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.RestoreFailed);
        return gate == SchemaGate.TooOld ? new CellHealthState(CellHealthStatus.Locked) : Available;
    }
}
