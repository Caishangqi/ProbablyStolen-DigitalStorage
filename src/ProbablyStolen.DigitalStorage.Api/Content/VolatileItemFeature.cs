using System;

namespace ProbablyStolen.DigitalStorage.Api.Content;

/// <summary>
/// An item feature in the builtin <see cref="DigitalStorageRegistries.VolatileItemFeature"/> registry that is not part
/// of an item's stored type. Register features another mod adds or removes on items without changing what they are,
/// such as price displays it rewrites whenever a price is read. Items that differ only in such features are one type in
/// storage, so they stack together and match the same partition rules; the features themselves stay on the items.
/// <para>
/// The entry id's namespace must be the id of the contributing batch's owner mod. The registry freezes before any game
/// loads, so the rules are the same for the whole session.
/// </para>
/// </summary>
public sealed class VolatileItemFeature
{
    /// <summary>Creates a rule for features whose identifier starts with <paramref name="identifierPrefix"/>.</summary>
    /// <param name="identifierPrefix">An ordinal, case-sensitive prefix of <c>ItemFeature.identifier</c>.</param>
    /// <exception cref="ArgumentException">The prefix is empty or contains white space.</exception>
    public VolatileItemFeature(string identifierPrefix)
    {
        if (string.IsNullOrEmpty(identifierPrefix))
            throw new ArgumentException("A feature identifier prefix is required.", nameof(identifierPrefix));
        foreach (var character in identifierPrefix)
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("A feature identifier prefix has no white space.", nameof(identifierPrefix));
        IdentifierPrefix = identifierPrefix;
    }

    /// <summary>Gets the ordinal, case-sensitive feature identifier prefix.</summary>
    public string IdentifierPrefix { get; }
}
