using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>The result of adding a rule to the grid.</summary>
public enum DraftAddStatus
{
    /// <summary>The rule was added at <see cref="DraftAddResult.Slot"/>.</summary>
    Added = 0,

    /// <summary>An identical default rule already exists at <see cref="DraftAddResult.Slot"/>; nothing was added.</summary>
    Duplicate = 1,

    /// <summary>The rule count reached the effective type limit; nothing was added.</summary>
    Full = 2
}

/// <summary>The outcome of an add: what happened and which slot to highlight.</summary>
public readonly struct DraftAddResult
{
    internal DraftAddResult(DraftAddStatus status, int slot)
    {
        Status = status;
        Slot = slot;
    }

    /// <summary>Gets what happened.</summary>
    public DraftAddStatus Status { get; }

    /// <summary>Gets the slot to highlight, or -1.</summary>
    public int Slot { get; }
}

/// <summary>Slot arithmetic of the rule grid.</summary>
public static class RuleGridOps
{
    /// <summary>
    /// Gets the free slot nearest to <paramref name="origin"/> by grid distance (rows plus columns), the smaller slot
    /// on a tie, or -1 when every slot below <paramref name="capacity"/> is taken.
    /// </summary>
    public static int NearestFree(int origin, ISet<int> occupied, int capacity, int columns)
    {
        if (occupied == null) throw new ArgumentNullException(nameof(occupied));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns), columns, "A grid has at least one column.");
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var slot = 0; slot < capacity; slot++)
        {
            if (occupied.Contains(slot)) continue;
            var distance = Math.Abs(slot / columns - origin / columns) + Math.Abs(slot % columns - origin % columns);
            if (distance < bestDistance)
            {
                best = slot;
                bestDistance = distance;
            }
        }

        return best;
    }
}

/// <summary>
/// A partition draft with positioned rules and an undo stack for removals. Every change raises <see cref="Changed"/>
/// for immediate saving. Slots only order the icons and never affect matching.
/// </summary>
public sealed class PartitionDraft
{
    private readonly Func<string> _newId;
    private readonly int _columns;
    private readonly Dictionary<int, PartitionRule> _rules = new();
    private readonly Stack<IReadOnlyList<PartitionRule>> _undo = new();
    private string _baselineText = string.Empty;

    /// <summary>Opens a draft of a stored configuration.</summary>
    /// <param name="baseline">The configuration in force.</param>
    /// <param name="ruleLimit">The rule slot limit, which is the cell's effective type limit.</param>
    /// <param name="columns">The grid width used to find the nearest free slot.</param>
    /// <param name="newId">Creates stable rule identifiers.</param>
    public PartitionDraft(PartitionConfig baseline, int ruleLimit, int columns, Func<string> newId)
    {
        if (ruleLimit < 1) throw new ArgumentOutOfRangeException(nameof(ruleLimit), ruleLimit, "A rule limit is positive.");
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns), columns, "A grid has at least one column.");
        _newId = newId ?? throw new ArgumentNullException(nameof(newId));
        _columns = columns;
        RuleLimit = ruleLimit;
        Rebase(baseline ?? throw new ArgumentNullException(nameof(baseline)));
    }

    /// <summary>Gets the configuration the draft was opened or last applied from.</summary>
    public PartitionConfig Baseline { get; private set; } = PartitionConfig.Empty;

    /// <summary>Gets the mode.</summary>
    public PartitionMode Mode { get; private set; }

    /// <summary>Raised after every change of the mode or the rules; the limit is not part of the stored partition.</summary>
    public event Action? Changed;

    /// <summary>Gets the rule slot limit: the cell's effective type limit, which follows its installed modules.</summary>
    public int RuleLimit { get; private set; }

    /// <summary>Gets the rules ordered by slot.</summary>
    public IReadOnlyList<PartitionRule> Rules => _rules.Values.OrderBy(rule => rule.Slot).ToList();

    /// <summary>Gets whether one more rule may be added; a lowered limit keeps existing rules but stops additions.</summary>
    public bool CanAdd => _rules.Count < RuleLimit;

    /// <summary>Gets the number of slots the grid shows: the limit, or more when kept rules sit beyond it.</summary>
    public int Capacity => Math.Max(RuleLimit, _rules.Count == 0 ? 0 : _rules.Keys.Max() + 1);

    /// <summary>Gets whether the partition differs from the last saved configuration, as after a failed save.</summary>
    public bool IsDirty => Mode != Baseline.Mode || PartitionRuleCodec.Encode(Rules) != _baselineText;

    /// <summary>Gets whether a removal can be undone.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets the rule at a slot, or null.</summary>
    public PartitionRule? At(int slot) => _rules.TryGetValue(slot, out var rule) ? rule : null;

    /// <summary>Sets the mode.</summary>
    public void SetMode(PartitionMode mode)
    {
        if (!Enum.IsDefined(typeof(PartitionMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode.");
        if (Mode == mode) return;
        Mode = mode;
        Notify();
    }

    /// <summary>Switches between whitelist and blacklist.</summary>
    public void ToggleMode() => SetMode(Mode == PartitionMode.Whitelist ? PartitionMode.Blacklist : PartitionMode.Whitelist);

    /// <summary>
    /// Updates the rule limit after a module change. A lower limit keeps every rule but stops additions.
    /// </summary>
    public void SetRuleLimit(int ruleLimit)
    {
        if (ruleLimit < 1) throw new ArgumentOutOfRangeException(nameof(ruleLimit), ruleLimit, "A rule limit is positive.");
        RuleLimit = ruleLimit;
    }

    /// <summary>
    /// Adds the default rule for a dragged sample. An identical default rule is located instead; a drop on an
    /// occupied slot goes to the nearest free slot and never replaces the rule there.
    /// </summary>
    public DraftAddResult AddSample(ItemKey sample, int dropSlot)
    {
        if (sample == null) throw new ArgumentNullException(nameof(sample));
        var existing = _rules.Values.FirstOrDefault(rule => rule.IsDefaultFor(sample.Identifier));
        if (existing != null) return new DraftAddResult(DraftAddStatus.Duplicate, existing.Slot);
        return Insert(dropSlot, slot => PartitionRule.ForSample(_newId(), slot, sample));
    }

    /// <summary>Copies a rule under a new identifier into the nearest free slot.</summary>
    public DraftAddResult Duplicate(int slot)
    {
        var source = At(slot) ?? throw new ArgumentException("No rule at this slot.", nameof(slot));
        return Insert(slot, target => source.With(slot: target, ruleId: _newId()));
    }

    /// <summary>Moves a rule to an empty slot, or swaps it with the rule at an occupied one.</summary>
    public bool Move(int fromSlot, int toSlot)
    {
        if (fromSlot == toSlot || toSlot < 0 || toSlot >= Capacity) return false;
        var moving = At(fromSlot);
        if (moving == null) return false;
        var other = At(toSlot);
        _rules.Remove(fromSlot);
        if (other != null)
        {
            _rules.Remove(toSlot);
            _rules[fromSlot] = other.With(slot: fromSlot);
        }

        _rules[toSlot] = moving.With(slot: toSlot);
        Notify();
        return true;
    }

    /// <summary>Renames a rule; an empty name clears it.</summary>
    public bool Rename(int slot, string? name)
    {
        var rule = At(slot);
        if (rule == null) return false;
        _rules[slot] = string.IsNullOrWhiteSpace(name) ? rule.With(clearName: true) : rule.With(name: name!.Trim());
        Notify();
        return true;
    }

    /// <summary>Replaces a rule with its edited copy from the detail page, keeping its slot.</summary>
    public void Replace(PartitionRule edited)
    {
        if (edited == null) throw new ArgumentNullException(nameof(edited));
        var current = _rules.Values.FirstOrDefault(rule => rule.RuleId == edited.RuleId)
            ?? throw new ArgumentException("The rule is no longer in the draft.", nameof(edited));
        _rules[current.Slot] = edited.With(slot: current.Slot);
        Notify();
    }

    /// <summary>Removes a rule; the removal can be undone.</summary>
    public bool Remove(int slot)
    {
        var rule = At(slot);
        if (rule == null) return false;
        _rules.Remove(slot);
        _undo.Push(new[] { rule });
        Notify();
        return true;
    }

    /// <summary>Removes every rule; the removal can be undone as one step.</summary>
    public void Clear()
    {
        if (_rules.Count == 0) return;
        _undo.Push(_rules.Values.ToList());
        _rules.Clear();
        Notify();
    }

    /// <summary>
    /// Restores the last removal at its former slots; a slot taken since then sends that rule to the nearest free one.
    /// Undo restores what was removed even beyond a lowered limit, since those rules existed before.
    /// </summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        foreach (var rule in _undo.Pop())
        {
            var slot = _rules.ContainsKey(rule.Slot)
                ? RuleGridOps.NearestFree(rule.Slot, new HashSet<int>(_rules.Keys), Capacity + 1, _columns)
                : rule.Slot;
            _rules[slot] = rule.With(slot: slot);
        }

        Notify();
        return true;
    }

    /// <summary>Gets the draft as a configuration at the baseline revision.</summary>
    public PartitionConfig ToConfig() => new(Mode, Rules, Baseline.Revision);

    /// <summary>
    /// Keeps the partition but takes the stored revision as its base, after a save whose outcome could not be confirmed
    /// left a newer revision; the next save then succeeds instead of reporting a conflict.
    /// </summary>
    public void AdoptRevision(int revision) => Baseline = Baseline.With(revision: revision);

    /// <summary>Records a saved configuration as the baseline; the rules and the undo stack stay as they are.</summary>
    public void MarkSaved(PartitionConfig saved)
    {
        Baseline = saved ?? throw new ArgumentNullException(nameof(saved));
        _baselineText = PartitionRuleCodec.Encode(saved.Rules);
    }

    /// <summary>Replaces the partition with a stored configuration, as when the cell changed elsewhere or a save failed.</summary>
    public void Rebase(PartitionConfig applied)
    {
        Baseline = applied ?? throw new ArgumentNullException(nameof(applied));
        Mode = applied.Mode;
        _rules.Clear();
        foreach (var rule in applied.Rules) _rules[rule.Slot] = rule;
        _baselineText = PartitionRuleCodec.Encode(Rules);
        _undo.Clear();
    }

    /// <summary>
    /// Gets the slot a new rule dropped at <paramref name="origin"/> lands on: the slot itself when free, else the
    /// nearest free one; -1 when nothing can be added. The drag highlight shows this slot.
    /// </summary>
    public int LandingSlot(int origin)
    {
        if (!CanAdd) return -1;
        return origin >= 0 && origin < Capacity && !_rules.ContainsKey(origin)
            ? origin
            : RuleGridOps.NearestFree(Math.Max(0, origin), new HashSet<int>(_rules.Keys), Capacity, _columns);
    }

    private DraftAddResult Insert(int origin, Func<int, PartitionRule> create)
    {
        var slot = LandingSlot(origin);
        if (slot < 0) return new DraftAddResult(DraftAddStatus.Full, -1);
        _rules[slot] = create(slot);
        Notify();
        return new DraftAddResult(DraftAddStatus.Added, slot);
    }

    private void Notify() => Changed?.Invoke();
}
