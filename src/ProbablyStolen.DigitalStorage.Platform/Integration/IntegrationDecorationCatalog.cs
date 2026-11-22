using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Platform.Integration;

internal sealed class IntegrationDecorationCatalog
{
    private readonly Dictionary<string, IReadOnlyList<Contribution>> _active = new(StringComparer.Ordinal);
    private readonly Action<string>? _notificationFailed;
    private Action? _checkDependencies;
    private Action<string, string, string, string?> _fault;

    internal IntegrationDecorationCatalog(Action<string, string, string, string?>? fault = null,
        Action<string>? notificationFailed = null)
    {
        _fault = fault ?? ((_, _, _, _) => { });
        _notificationFailed = notificationFailed;
    }

    internal event Action? Changed;

    internal void BindHost(Action checkDependencies, Action<string, string, string, string?> fault)
    {
        _checkDependencies = checkDependencies ?? throw new ArgumentNullException(nameof(checkDependencies));
        _fault = fault ?? throw new ArgumentNullException(nameof(fault));
    }

    internal long Revision { get; private set; }
    internal bool HasActive => _active.Count > 0;

    internal void Activate(string pluginId, IReadOnlyList<Contribution> contributions)
    {
        if (contributions.Count == 0)
        {
            Withdraw(pluginId);
            return;
        }
        _active[pluginId] = contributions.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
        NotifyChanged();
    }

    internal void Withdraw(string pluginId)
    {
        if (_active.Remove(pluginId)) NotifyChanged();
    }

    internal void Invalidate(string pluginId)
    {
        if (_active.ContainsKey(pluginId)) NotifyChanged();
    }

    internal void OnSettingsApplied()
    {
        if (HasActive) NotifyChanged();
    }

    /// <summary>Resolves only the visible representatives and returns detached results aligned to slot indices.</summary>
    internal IReadOnlyList<IReadOnlyList<SlotDecoration>>? EvaluatePage(StorageScreenPage page, bool storageAvailable,
        Func<ItemKey, GameItem?> findSample)
    {
        if (!storageAvailable || page.State != TerminalDisplayState.Ready || !CheckActive()) return null;
        var slotIndices = new List<int>();
        var samples = new List<GameItem>();
        foreach (var slot in page.Slots)
        {
            if (slot.Group is not { } group || group.Representative.Amount <= 0) continue;
            var sample = findSample(group.Representative.Key);
            if (sample == null) continue;
            slotIndices.Add(slot.Index);
            samples.Add(sample);
        }

        if (samples.Count == 0) return null;
        var external = EvaluateActive(samples);
        var result = new IReadOnlyList<SlotDecoration>[page.Slots.Count];
        Array.Fill(result, Array.Empty<SlotDecoration>());
        for (var index = 0; index < slotIndices.Count; index++) result[slotIndices[index]] = external[index];
        return result;
    }

    internal IReadOnlyList<IReadOnlyList<SlotDecoration>> Evaluate(IReadOnlyList<GameItem> samples)
    {
        CheckActive();
        return EvaluateActive(samples);
    }

    private bool CheckActive()
    {
        if (HasActive) _checkDependencies?.Invoke();
        return HasActive;
    }

    private IReadOnlyList<IReadOnlyList<SlotDecoration>> EvaluateActive(IReadOnlyList<GameItem> samples)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        var results = Enumerable.Range(0, samples.Count).Select(_ => new List<SlotDecoration>()).ToArray();
        if (samples.Count == 0 || !HasActive) return results;
        var batch = Array.AsReadOnly(samples.ToArray());
        foreach (var plugin in _active.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray())
        {
            if (!IsCurrent(plugin)) continue;
            var pending = Enumerable.Range(0, samples.Count).Select(_ => new List<SlotDecoration>()).ToArray();
            foreach (var contribution in plugin.Value)
            {
                try
                {
                    var tints = contribution.Evaluate(batch) ?? throw new InvalidOperationException("null_result");
                    if (tints.Count != samples.Count) throw new InvalidOperationException("result_count");
                    for (var index = 0; index < samples.Count; index++)
                    {
                        if (tints[index] is not int tint) continue;
                        if (tint is < 0 or > 0xFFFFFF) throw new InvalidOperationException("invalid_rgb");
                        pending[index].Add(new SlotDecoration(contribution.OverlayKey, tint));
                    }
                }
                catch (Exception error)
                {
                    if (IsCurrent(plugin))
                    {
                        Withdraw(plugin.Key);
                        _fault(plugin.Key, contribution.Id, "decoration_evaluate_failed", error.GetType().Name);
                    }
                    break;
                }
                if (!IsCurrent(plugin)) break;
            }

            if (!IsCurrent(plugin)) continue;
            for (var index = 0; index < samples.Count; index++) results[index].AddRange(pending[index]);
        }

        return results;
    }

    private bool IsCurrent(KeyValuePair<string, IReadOnlyList<Contribution>> plugin) =>
        _active.TryGetValue(plugin.Key, out var active) && ReferenceEquals(active, plugin.Value);

    private void NotifyChanged()
    {
        Revision++;
        if (Changed is not { } subscribers) return;
        foreach (Action subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(); }
            catch (Exception error)
            {
                // This also runs in native postfixes. A broken view must not affect the publisher or other views.
                try { _notificationFailed?.Invoke(error.GetType().Name); }
                catch { }
            }
        }
    }

    internal sealed class Contribution
    {
        internal Contribution(string id, string overlayKey,
            Func<IReadOnlyList<GameItem>, IReadOnlyList<int?>> evaluate) =>
            (Id, OverlayKey, Evaluate) = (id, overlayKey, evaluate);

        internal string Id { get; }
        internal string OverlayKey { get; }
        internal Func<IReadOnlyList<GameItem>, IReadOnlyList<int?>> Evaluate { get; }
    }
}
