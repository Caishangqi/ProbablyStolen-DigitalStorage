using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>The raw stored partition of a cell, exactly as written, so a failed write can be undone.</summary>
public sealed class PartitionSnapshot : IEquatable<PartitionSnapshot>
{
    /// <summary>Creates a snapshot; null means the tag is absent.</summary>
    public PartitionSnapshot(string? mode, string? rules, int? revision, bool unconfirmed)
    {
        Mode = mode;
        Rules = rules;
        Revision = revision;
        Unconfirmed = unconfirmed;
    }

    /// <summary>Gets the mode tag text.</summary>
    public string? Mode { get; }

    /// <summary>Gets the rules tag text.</summary>
    public string? Rules { get; }

    /// <summary>Gets the revision tag value.</summary>
    public int? Revision { get; }

    /// <summary>Gets whether the unconfirmed flag is set.</summary>
    public bool Unconfirmed { get; }

    /// <summary>
    /// Interprets the snapshot, removing what is damaged instead of locking the cell: a rule that cannot be read, is
    /// invalid, or repeats an earlier rule's identifier or slot is dropped and the others stay in force; an unknown
    /// mode reads as the default whitelist; a leftover unconfirmed flag is ignored. The next save writes the cleaned
    /// configuration.
    /// </summary>
    public PartitionReadResult Interpret()
    {
        var mode = CellRecord.PartitionMode(Mode);
        var dropped = 0;
        var kept = new List<PartitionRule>();
        if (Rules != null)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var slots = new HashSet<int>();
            foreach (var rule in PartitionRuleCodec.DecodeReadable(Rules, out dropped))
            {
                if (rule.Validate() != null || !ids.Add(rule.RuleId) || !slots.Add(rule.Slot)) dropped++;
                else kept.Add(rule);
            }
        }

        var config = new PartitionConfig(mode ?? PartitionMode.Whitelist, kept, Revision ?? 0);
        return new PartitionReadResult(config, dropped, mode == null || Unconfirmed || dropped > 0);
    }

    /// <inheritdoc />
    public bool Equals(PartitionSnapshot? other) => other != null && Mode == other.Mode && Rules == other.Rules &&
        Revision == other.Revision && Unconfirmed == other.Unconfirmed;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PartitionSnapshot);

    /// <inheritdoc />
    public override int GetHashCode() => (Mode, Rules, Revision, Unconfirmed).GetHashCode();
}

/// <summary>A configuration read from a cell, with what was removed as damaged.</summary>
public sealed class PartitionReadResult
{
    internal PartitionReadResult(PartitionConfig config, int droppedRules, bool repaired)
    {
        Config = config;
        DroppedRules = droppedRules;
        Repaired = repaired;
    }

    /// <summary>Gets the usable configuration; its revision is the stored one, which a commit must match.</summary>
    public PartitionConfig Config { get; }

    /// <summary>Gets how many stored rules were damaged and removed.</summary>
    public int DroppedRules { get; }

    /// <summary>Gets whether anything stored was damaged and removed or replaced by a default.</summary>
    public bool Repaired { get; }
}

/// <summary>Reads and writes one cell's partition tags; implemented over the native item.</summary>
public interface ICellPartitionStore
{
    /// <summary>Gets the stored tags.</summary>
    PartitionSnapshot Capture();

    /// <summary>Replaces the complete partition tag set as one native state publication.</summary>
    void Replace(PartitionSnapshot snapshot);
}

/// <summary>The outcome of applying a draft.</summary>
public enum PartitionCommitStatus
{
    /// <summary>The draft is stored and verified.</summary>
    Applied = 0,

    /// <summary>The draft has problems and was not written.</summary>
    Invalid = 1,

    /// <summary>Another change was applied since the draft was opened; nothing was written.</summary>
    Conflict = 2,

    /// <summary>Writing failed; the store's atomic replacement left the previous configuration in force.</summary>
    Failed = 3
}

/// <summary>The result of <see cref="PartitionCommit.Apply"/>.</summary>
public sealed class PartitionCommitResult
{
    internal PartitionCommitResult(PartitionCommitStatus status, PartitionConfig? applied)
    {
        Status = status;
        Applied = applied;
    }

    /// <summary>Gets the outcome.</summary>
    public PartitionCommitStatus Status { get; }

    /// <summary>Gets the stored configuration after <see cref="PartitionCommitStatus.Applied"/>, the new baseline.</summary>
    public PartitionConfig? Applied { get; }
}

/// <summary>
/// Saves a partition configuration after a revision check. The store replaces the whole tag set in one publication,
/// so a failed save leaves the previous configuration in force and nothing partial is ever read.
/// </summary>
public static class PartitionCommit
{
    /// <summary>Applies a draft opened at <paramref name="baseRevision"/>.</summary>
    public static PartitionCommitResult Apply(ICellPartitionStore store, PartitionConfig draft, int baseRevision)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (draft == null) throw new ArgumentNullException(nameof(draft));
        if (draft.Validate().Count > 0) return new PartitionCommitResult(PartitionCommitStatus.Invalid, null);

        var before = store.Capture();
        if ((before.Revision ?? 0) != baseRevision) return new PartitionCommitResult(PartitionCommitStatus.Conflict, null);

        var target = draft.With(revision: checked(baseRevision + 1));
        var written = new PartitionSnapshot(CellRecord.PartitionModeText(target.Mode),
            target.Rules.Count == 0 ? null : PartitionRuleCodec.Encode(target.Rules), target.Revision, unconfirmed: false);
        try
        {
            store.Replace(written);
            return new PartitionCommitResult(PartitionCommitStatus.Applied, target);
        }
        catch (Exception)
        {
            // The store contract publishes atomically, so a failed replacement leaves the prior snapshot intact.
            return new PartitionCommitResult(PartitionCommitStatus.Failed, null);
        }
    }

    /// <summary>Gets whether a configuration has any usable rule, which blocks disassembly.</summary>
    public static bool HasRules(PartitionSnapshot snapshot) => snapshot != null && snapshot.Interpret().Config.Rules.Count > 0;
}
