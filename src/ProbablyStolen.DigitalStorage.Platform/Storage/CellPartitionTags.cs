using System;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// The partition tags of one cell. The mode tag stays on the housing through disassembly; the rules, revision and
/// unconfirmed flag belong to the assembled cell, and disassembly requires the rules to be empty. Tags are part of
/// the native item state, so the save graph keeps them with the cell.
/// </summary>
internal sealed class CellPartitionTags : ICellPartitionStore
{
    internal const string ModeTag = "digital_storage.partition_mode";
    internal const string RulesTag = "digital_storage.partition_rules";
    internal const string RevisionTag = "digital_storage.partition_revision";
    internal const string UnconfirmedTag = "digital_storage.partition_unconfirmed";

    // A write removes obsolete exact identity partition tags without converting them.
    private const string LegacyKeysTag = "digital_storage.partition";
    private const string LegacyEqualTag = "digital_storage.partition_equal";

    private readonly GameItem _cell;

    internal CellPartitionTags(GameItem cell) => _cell = cell;

    /// <summary>Gets the effective partition; damaged rules are left out and the rest stay in force.</summary>
    internal static CellPartition Read(GameItem cell) =>
        CellPartition.From(new CellPartitionTags(cell).Capture().Interpret().Config, CanonicalAttributeSource.Instance);

    /// <summary>Gets whether the cell has usable rules, which block disassembly.</summary>
    internal static bool HasRules(GameItem cell) => PartitionCommit.HasRules(new CellPartitionTags(cell).Capture());

    /// <inheritdoc />
    public PartitionSnapshot Capture() => new(
        _cell.IsTag(ModeTag) ? _cell.GetTagReadonly(ModeTag).GetString() ?? string.Empty : null,
        _cell.IsTag(RulesTag) ? _cell.GetTagReadonly(RulesTag).GetString() ?? string.Empty : null,
        _cell.IsTag(RevisionTag) ? _cell.GetTagReadonly(RevisionTag).GetInt() : null,
        _cell.IsTag(UnconfirmedTag));

    /// <inheritdoc />
    public void Replace(PartitionSnapshot snapshot)
    {
        var original = _cell.state ?? throw new InvalidOperationException("The native item state is unavailable.");
        var committed = CloneState(original);
        SetString(committed, ModeTag, snapshot.Mode);
        SetString(committed, RulesTag, snapshot.Rules);
        SetInt(committed, RevisionTag, snapshot.Revision);
        SetInt(committed, UnconfirmedTag, snapshot.Unconfirmed ? 1 : null);
        SetInt(committed, LegacyKeysTag, null);
        SetInt(committed, LegacyEqualTag, null);

        var visible = CloneState(_cell.modifiedState ?? original);
        SetString(visible, ModeTag, snapshot.Mode);
        SetString(visible, RulesTag, snapshot.Rules);
        SetInt(visible, RevisionTag, snapshot.Revision);
        SetInt(visible, UnconfirmedTag, snapshot.Unconfirmed ? 1 : null);
        SetInt(visible, LegacyKeysTag, null);
        SetInt(visible, LegacyEqualTag, null);

        _cell.state = committed;
        _cell.modifiedState = visible;
    }

    internal void NotifyCommitted(Action<string> reportPresentationFailure)
    {
        if (reportPresentationFailure == null) throw new ArgumentNullException(nameof(reportPresentationFailure));
        try
        {
            _cell.ValidateShapeState();
        }
        catch (Exception error)
        {
            reportPresentationFailure($"Digital Storage: partition saved but native presentation refresh failed ({error.GetType().Name}).");
        }
        Committed?.Invoke(_cell);
    }

    /// <summary>Raised after a partition change is saved on a cell.</summary>
    internal static event Action<GameItem>? Committed;

    private static TagSystem CloneState(TagSystem source)
    {
        var clone = new TagSystem();
        foreach (var pair in source.dict)
            clone.dict.Add(pair.Key, pair.Value.Clone());
        return clone;
    }

    private static void SetString(TagSystem state, string key, string? value)
    {
        if (value == null) state.DisableTag(key);
        else state.GetTag(key).Enable().SetString(value);
    }

    private static void SetInt(TagSystem state, string key, int? value)
    {
        if (value == null) state.DisableTag(key);
        else state.GetTag(key).Enable().SetInt(value.Value);
    }
}
