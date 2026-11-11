namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Selects how a non-empty partition list filters identities.</summary>
/// <remarks>An empty list accepts every identity in both modes, so "unfiltered" is not a separate mode.</remarks>
public enum IncludeExclude
{
    /// <summary>Accepts only listed identities.</summary>
    Whitelist = 0,

    /// <summary>Accepts every identity except listed ones.</summary>
    Blacklist = 1
}
