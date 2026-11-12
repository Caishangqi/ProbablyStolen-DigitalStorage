using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>
/// Attribute rules combine with OR and preserve unknown results. An identity fixes every attribute the rules read,
/// so results are cached per identity for the lifetime of this list, which is one configuration revision.
/// </summary>
public sealed class RulePartitionList : IPartitionList
{
    private readonly IReadOnlyList<PartitionRule> _rules;
    private readonly IItemAttributeSource _attributes;
    private readonly Dictionary<ItemKey, PartitionMatch> _cache = new();

    /// <summary>Creates a list over valid rules.</summary>
    public RulePartitionList(IEnumerable<PartitionRule> rules, IItemAttributeSource attributes)
    {
        _rules = (rules ?? throw new ArgumentNullException(nameof(rules))).ToList().AsReadOnly();
        if (_rules.Any(rule => rule == null)) throw new ArgumentException("A rule cannot be null.", nameof(rules));
        _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    /// <summary>Gets a list without rules.</summary>
    public static RulePartitionList Empty { get; } = new(Array.Empty<PartitionRule>(), CanonicalAttributeSource.Instance);

    /// <summary>Gets the rules.</summary>
    public IReadOnlyList<PartitionRule> Rules => _rules;

    /// <inheritdoc />
    public bool IsEmpty() => _rules.Count == 0;

    /// <inheritdoc />
    public bool IsListed(ItemKey key) => key != null && Evaluate(key) == PartitionMatch.Match;

    /// <inheritdoc />
    public PartitionMatch Evaluate(ItemKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var attributes = _attributes.Read(key);
        var result = PartitionMatch.NoMatch;
        foreach (var rule in _rules)
        {
            var match = rule.Evaluate(attributes);
            if (match == PartitionMatch.Match)
            {
                result = PartitionMatch.Match;
                break;
            }

            if (match == PartitionMatch.Unknown) result = PartitionMatch.Unknown;
        }

        _cache[key] = result;
        return result;
    }
}
