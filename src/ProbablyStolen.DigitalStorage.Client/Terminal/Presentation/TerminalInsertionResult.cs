namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Explains the result of storing supported player inventory stacks.</summary>
public enum TerminalInsertionStatus
{
    /// <summary>At least one complete stack was stored.</summary>
    Success,

    /// <summary>The current network is offline.</summary>
    Offline,

    /// <summary>No supported complete stack was available.</summary>
    NoItems,

    /// <summary>Every supported stack was rejected by capacity or state rules.</summary>
    Rejected,

    /// <summary>A storage commit failed and was recovered where possible.</summary>
    Failed
}

/// <summary>Immutable feedback for a terminal insertion action.</summary>
public sealed class TerminalInsertionResult
{
    internal TerminalInsertionResult(TerminalInsertionStatus status, long storedAmount, string messageKey)
    {
        Status = status;
        StoredAmount = storedAmount;
        MessageKey = messageKey;
    }

    /// <summary>Gets the insertion status.</summary>
    public TerminalInsertionStatus Status { get; }

    /// <summary>Gets the quantity stored from player inventory.</summary>
    public long StoredAmount { get; }

    /// <summary>Gets the localization key for feedback.</summary>
    public string MessageKey { get; }

    /// <summary>Creates a result for a platform adapter.</summary>
    public static TerminalInsertionResult Create(TerminalInsertionStatus status,
        long storedAmount, string messageKey) => new(status, storedAmount, messageKey);
}
