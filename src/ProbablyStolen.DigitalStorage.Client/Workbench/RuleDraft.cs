using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>One editable condition row. Unlike a stored condition it may still lack values.</summary>
public sealed class ConditionRow
{
    internal ConditionRow(PartitionField field, PartitionOperator op, PartitionValue? target, PartitionValue? upper)
    {
        Field = field;
        Operator = op;
        Target = target;
        Upper = upper;
    }

    /// <summary>Gets the field.</summary>
    public PartitionField Field { get; internal set; }

    /// <summary>Gets the operator.</summary>
    public PartitionOperator Operator { get; internal set; }

    /// <summary>Gets the target or lower bound, or null when not entered yet.</summary>
    public PartitionValue? Target { get; internal set; }

    /// <summary>Gets the upper bound of a range, or null when not entered yet.</summary>
    public PartitionValue? Upper { get; internal set; }

    /// <summary>Gets the stored condition this row stands for.</summary>
    public PartitionCondition ToCondition() => new(Field.Id, Operator, Target, Upper);

    /// <summary>Gets why the row cannot be saved, or null.</summary>
    public PartitionIssue? Validate() => ToCondition().Validate();
}

/// <summary>
/// Editable condition rows for one rule. <see cref="Complete"/> returns a copy for the overview draft.
/// </summary>
public sealed class RuleDraft
{
    private readonly PartitionRule _original;
    private readonly ItemAttributeSet? _sample;
    private readonly List<ConditionRow> _rows;

    /// <summary>Opens a rule; the sample, when known, supplies initial values of added conditions.</summary>
    public RuleDraft(PartitionRule rule, ItemAttributeSet? sample)
    {
        _original = rule ?? throw new ArgumentNullException(nameof(rule));
        _sample = sample;
        Name = rule.Name;
        _rows = rule.Conditions.Select(condition =>
        {
            var field = PartitionFieldRegistry.Find(condition.FieldId)
                ?? throw new ArgumentException($"Unknown field '{condition.FieldId}'.", nameof(rule));
            return new ConditionRow(field, condition.Operator, condition.Target, condition.Upper);
        }).ToList();
    }

    /// <summary>Gets the rows in order.</summary>
    public IReadOnlyList<ConditionRow> Rows => _rows;

    /// <summary>Gets or sets the rule name; empty clears it.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Adds a condition with the field's default operator and the sample's visible value. Missing values and
    /// samples with several types leave the target unset.
    /// </summary>
    public ConditionRow AddCondition(string fieldId)
    {
        var row = NewRow(fieldId);
        _rows.Add(row);
        return row;
    }

    /// <summary>Switches a row to another field, resetting its operator and value so no hidden old condition remains.</summary>
    public void ChangeField(int index, string fieldId)
    {
        Row(index);
        _rows[index] = NewRow(fieldId);
    }

    /// <summary>Sets a row's operator; entering or leaving a range adds or drops the upper bound.</summary>
    public void SetOperator(int index, PartitionOperator op)
    {
        var row = Row(index);
        if (!row.Field.Supports(op)) throw new ArgumentException($"{row.Field.Id} does not support {op}.", nameof(op));
        row.Operator = op;
        if (op != PartitionOperator.Range) row.Upper = null;
    }

    /// <summary>Sets a row's target or lower bound; null clears it.</summary>
    public void SetTarget(int index, PartitionValue? value) => Row(index).Target = value;

    /// <summary>Sets a range's upper bound; null clears it.</summary>
    public void SetUpper(int index, PartitionValue? value)
    {
        var row = Row(index);
        if (row.Operator != PartitionOperator.Range) throw new InvalidOperationException("Only a range has an upper bound.");
        row.Upper = value;
    }

    /// <summary>Removes a row.</summary>
    public void Remove(int index)
    {
        Row(index);
        _rows.RemoveAt(index);
    }

    /// <summary>Gets why the rule cannot be completed, or null: a rule needs at least one complete condition.</summary>
    public PartitionIssue? Validate()
    {
        if (_rows.Count == 0) return PartitionIssue.NoConditions;
        return _rows.Select(row => row.Validate()).FirstOrDefault(issue => issue != null);
    }

    /// <summary>Gets the edited rule for the overview draft.</summary>
    /// <exception cref="InvalidOperationException">The rule is not valid yet.</exception>
    public PartitionRule Complete()
    {
        if (Validate() != null) throw new InvalidOperationException("Finish every condition before completing the rule.");
        return new PartitionRule(_original.RuleId, _original.Slot, string.IsNullOrWhiteSpace(Name) ? null : Name!.Trim(),
            _original.DisplayKey, _rows.Select(row => row.ToCondition()));
    }

    /// <summary>Gets the warnings of the edited conditions, such as a contradiction that never matches.</summary>
    public IReadOnlyList<PartitionWarning> Lint()
    {
        if (Validate() != null) return Array.Empty<PartitionWarning>();
        return PartitionLint.Check(new[] { Complete() });
    }

    private ConditionRow Row(int index) => index >= 0 && index < _rows.Count
        ? _rows[index]
        : throw new ArgumentOutOfRangeException(nameof(index), index, "No condition at this row.");

    private ConditionRow NewRow(string fieldId)
    {
        var field = PartitionFieldRegistry.Find(fieldId) ?? throw new ArgumentException($"Unknown field '{fieldId}'.", nameof(fieldId));
        var op = field.Operators[0];
        if (!field.HasTarget) return new ConditionRow(field, op, null, null);
        var sample = _sample?.Get(field.Id);
        PartitionValue? target = null;
        if (sample is { IsKnown: true })
        {
            target = field.Kind switch
            {
                PartitionFieldKind.Number => PartitionValue.Of(sample.Number),
                PartitionFieldKind.TypeSet => sample.Texts.Count == 1 ? PartitionValue.Of(sample.Texts.First()) : null,
                _ => string.IsNullOrEmpty(sample.Text) ? null : PartitionValue.Of(sample.Text!)
            };
        }

        return new ConditionRow(field, op, target, null);
    }
}
