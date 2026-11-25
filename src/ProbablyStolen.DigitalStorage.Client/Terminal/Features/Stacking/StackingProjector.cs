using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// Groups the storage view by the terminal's item stacking rules while the module is installed and at least one rule is
/// active. A group whose piece count would overflow is shown as its separate entries.
/// </summary>
public sealed class StackingProjector : IStorageEntryProjector
{
    /// <summary>The projector id.</summary>
    public const string ProjectorId = "stacking";

    private static readonly ConditionalWeakTable<ITerminalFeatureData, StackingConfigStore> Stores = new();
    private static readonly ConditionalWeakTable<StackingConfigStore, KeyCache> Caches = new();

    private readonly Action<string> _diagnostic;
    private readonly Func<bool> _explainSplits;
    private readonly HashSet<string> _explained = new(StringComparer.Ordinal);

    /// <summary>Creates the projector.</summary>
    /// <param name="diagnostic">Receives split explanations.</param>
    /// <param name="explainSplits">Whether to explain why entries of one item stay in separate groups.</param>
    public StackingProjector(Action<string> diagnostic, Func<bool> explainSplits)
    {
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        _explainSplits = explainSplits ?? throw new ArgumentNullException(nameof(explainSplits));
    }

    /// <inheritdoc />
    public string Id => ProjectorId;

    /// <summary>Gets the values the projector compares beside the identity.</summary>
    public static IReadOnlyCollection<string> AttributeNames { get; } = new[] { StackingFieldCatalog.AppraisedValue };

    /// <inheritdoc />
    public IReadOnlyCollection<string> RequiredAttributes => AttributeNames;

    /// <summary>Gets the store of a terminal's data, shared by everything reading it during one window's life.</summary>
    public static StackingConfigStore StoreOf(ITerminalFeatureData data) =>
        Stores.GetValue(data ?? throw new ArgumentNullException(nameof(data)), key => new StackingConfigStore(key));

    /// <inheritdoc />
    public bool IsActive(TerminalContext context)
    {
        if (!StackingModule.IsInstalled(context)) return false;
        return StoreOf(context.FeatureData).Effective()?.HasActiveRule == true;
    }

    /// <inheritdoc />
    public IReadOnlyList<ProjectedGroup> Project(IReadOnlyList<TerminalEntry> entries, TerminalContext context,
        EntryAttributes attributes)
    {
        var store = StoreOf(context.FeatureData);
        var config = store.Effective() ?? StackingConfig.Default;
        var cache = Caches.GetValue(store, _ => new KeyCache());
        cache.Use(config);
        var buckets = new Dictionary<StackingGroupKey, List<TerminalEntry>>();
        var order = new List<StackingGroupKey>();
        foreach (var entry in entries)
        {
            var key = cache.Get(entry.Key, attributes.For(entry.Key),
                values => StackingProjection.Compute(entry.Key, config, new StackingAttributes(values)));
            if (!buckets.TryGetValue(key, out var members))
            {
                members = new List<TerminalEntry>();
                buckets.Add(key, members);
                order.Add(key);
            }

            members.Add(entry);
        }

        if (_explainSplits()) ExplainSplits(order, buckets, config, attributes);
        var groups = new List<ProjectedGroup>();
        foreach (var key in order)
        {
            var members = buckets[key];
            if (members.Count == 1 || !FitsCount(members))
                groups.AddRange(members.Select(member => new ProjectedGroup(IdentityProjection.IdOf(member), new[] { member })));
            else
                groups.Add(new ProjectedGroup(key.ToStableId(), members));
        }

        return groups;
    }

    /// <summary>
    /// Group keys of one terminal under one configuration. Decoding identities is the costly part of grouping, and the
    /// view regroups on every refresh, sort change and Shift press, so a key is computed again only when its settings
    /// or its read values change.
    /// </summary>
    private sealed class KeyCache
    {
        private readonly Dictionary<ItemKey, (IReadOnlyDictionary<string, long> Values, StackingGroupKey Key)> _keys = new();
        private StackingConfig? _config;

        internal void Use(StackingConfig config)
        {
            if (config.Equals(_config)) return;
            _config = config;
            _keys.Clear();
        }

        internal StackingGroupKey Get(ItemKey key, IReadOnlyDictionary<string, long> values,
            Func<IReadOnlyDictionary<string, long>, StackingGroupKey> compute)
        {
            if (_keys.TryGetValue(key, out var cached) && SameValues(cached.Values, values)) return cached.Key;
            var computed = compute(values);
            _keys[key] = (values, computed);
            return computed;
        }

        private static bool SameValues(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
            left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    // Logs, once per item and settings, which identity parts still keep entries of one item in separate groups. Only
    // section, tag and feature names are written, never their values.
    private void ExplainSplits(IReadOnlyList<StackingGroupKey> order,
        IReadOnlyDictionary<StackingGroupKey, List<TerminalEntry>> buckets, StackingConfig config, EntryAttributes attributes)
    {
        foreach (var item in order.GroupBy(key => key.Identifier).Where(group => group.Count() > 1))
        {
            var marker = item.Key + "|" + StackingConfigCodec.Encode(config);
            if (!_explained.Add(marker)) continue;
            var first = buckets[item.First()][0];
            var second = buckets[item.Skip(1).First()][0];
            var reason = StackingProjection.Explain(first.Key, new StackingAttributes(attributes.For(first.Key)),
                second.Key, new StackingAttributes(attributes.For(second.Key)), config);
            _diagnostic($"stacking_split item={item.Key} groups={item.Count()} differs={reason}");
        }
    }

    private static bool FitsCount(IEnumerable<TerminalEntry> members)
    {
        long pieces = 0;
        long units = 0;
        foreach (var member in members)
        {
            if (pieces > long.MaxValue - member.Amount || units > long.MaxValue - member.TotalUnits) return false;
            pieces += member.Amount;
            units += member.TotalUnits;
        }

        return true;
    }
}
