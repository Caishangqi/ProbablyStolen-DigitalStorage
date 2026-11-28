using System;

namespace ProbablyStolen.DigitalStorage.Api.Stacks;

/// <summary>An identity that cannot be encoded, with the rule that refused it and the part where it was met.</summary>
internal sealed class CanonicalIdentityRejectedException : ArgumentException
{
    internal CanonicalIdentityRejectedException(string rule, string at)
        : base("The identity input is invalid or unsupported (" + rule + " at " + at + ").")
    {
        Rule = rule;
        At = at;
    }

    /// <summary>Short code of the refusing rule, such as <c>text_too_long</c> or <c>record_limit</c>.</summary>
    internal string Rule { get; }

    /// <summary>Item identifier and, when known, the tag or feature, such as <c>coffee_bean/tag:KEY</c>.</summary>
    internal string At { get; }
}
