using System;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>Tells why a stored entry's identity could not be read, for explanations shown to the player.</summary>
public interface IItemIdentityDiagnostics
{
    /// <summary>Gets why the key stands in for an unreadable identity, or null when it was read normally.</summary>
    ItemIdentityFallback? FallbackOf(ItemKey key);
}

/// <summary>Why one item's state could not be read. Holds only codes and names, never stored values.</summary>
public sealed class ItemIdentityFallback
{
    /// <summary>Creates a reason.</summary>
    /// <param name="failure">The reading failure, such as <c>EncodingRejected</c>.</param>
    /// <param name="rule">The refusing rule of a rejected encoding, such as <c>record_limit</c>; otherwise null.</param>
    /// <param name="at">The item and the tag or feature where the rule was met; otherwise null.</param>
    public ItemIdentityFallback(string failure, string? rule = null, string? at = null)
    {
        Failure = failure ?? throw new ArgumentNullException(nameof(failure));
        Rule = rule;
        At = at;
    }

    /// <summary>Gets the reading failure.</summary>
    public string Failure { get; }

    /// <summary>Gets the refusing rule of a rejected encoding, or null.</summary>
    public string? Rule { get; }

    /// <summary>Gets where the rule was met, or null.</summary>
    public string? At { get; }
}
