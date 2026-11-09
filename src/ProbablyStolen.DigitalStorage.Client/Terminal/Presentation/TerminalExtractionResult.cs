namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Describes a terminal extraction request outcome without exposing native game objects.</summary>
public enum TerminalExtractionStatus
{
    /// <summary>The requested quantity was extracted and accepted by the player inventory.</summary>
    Success,

    /// <summary>The current network is offline or structurally unavailable.</summary>
    Offline,

    /// <summary>No matching quantity was available.</summary>
    NoItems,

    /// <summary>The requested quantity could not fit as one complete native stack.</summary>
    TargetRejected,

    /// <summary>The network could not pay for any quantity.</summary>
    InsufficientPower,

    /// <summary>The native item state could not be reconstructed safely.</summary>
    UnsupportedItem,

    /// <summary>An unexpected commit or recovery failure occurred.</summary>
    Failed,

    /// <summary>The requested quantity was not a positive amount.</summary>
    InvalidAmount,

    /// <summary>Some, but not all, of the requested quantity was extracted.</summary>
    Partial
}

/// <summary>Immutable terminal extraction feedback used by the native overlay.</summary>
public sealed class TerminalExtractionResult
{
    internal TerminalExtractionResult(TerminalExtractionStatus status, long requestedAmount,
        long extractedAmount, string messageKey)
    {
        Status = status;
        RequestedAmount = requestedAmount;
        ExtractedAmount = extractedAmount;
        MessageKey = messageKey;
    }

    /// <summary>Gets the operation status.</summary>
    public TerminalExtractionStatus Status { get; }

    /// <summary>Gets the requested amount.</summary>
    public long RequestedAmount { get; }

    /// <summary>Gets the amount actually extracted.</summary>
    public long ExtractedAmount { get; }

    /// <summary>Gets the localization key for player feedback.</summary>
    public string MessageKey { get; }

    /// <summary>Whether some, but not all, of the requested quantity was extracted.</summary>
    public bool IsPartial => ExtractedAmount > 0 && ExtractedAmount < RequestedAmount;

    /// <summary>Whether the full requested quantity succeeded.</summary>
    public bool Succeeded => Status == TerminalExtractionStatus.Success &&
        ExtractedAmount == RequestedAmount;

    /// <summary>Creates a feedback result for platform adapters.</summary>
    public static TerminalExtractionResult Create(TerminalExtractionStatus status,
        long requestedAmount, long extractedAmount, string messageKey) =>
        new(status, requestedAmount, extractedAmount, messageKey);
}
