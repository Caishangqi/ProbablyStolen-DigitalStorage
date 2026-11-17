using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>One validated physical-piece extraction step from a terminal display group.</summary>
public readonly struct TerminalDragStep
{
    internal TerminalDragStep(string groupId, long entrySerial, ItemKey key, long pieces)
    {
        if (string.IsNullOrEmpty(groupId)) throw new ArgumentException("A group id is required.", nameof(groupId));
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (entrySerial < 0) throw new ArgumentOutOfRangeException(nameof(entrySerial));
        if (pieces <= 0) throw new ArgumentOutOfRangeException(nameof(pieces));
        GroupId = groupId;
        EntrySerial = entrySerial;
        Key = key;
        Pieces = pieces;
    }

    /// <summary>Gets the display group that owns this step.</summary>
    public string GroupId { get; }
    /// <summary>Gets the stable repository serial for the exact entry.</summary>
    public long EntrySerial { get; }
    /// <summary>Gets the exact storage identity.</summary>
    public ItemKey Key { get; }
    /// <summary>Gets the requested physical-piece count.</summary>
    public long Pieces { get; }
}

/// <summary>An immutable, revision-bound batch of terminal drag extraction steps.</summary>
public sealed class TerminalDragBatch
{
    internal TerminalDragBatch(long viewRevision, long presentationRevision, IReadOnlyList<string> selectedGroupIds,
        IReadOnlyList<TerminalDragStep> steps, long requestedPieces)
    {
        ViewRevision = viewRevision;
        PresentationRevision = presentationRevision;
        SelectedGroupIds = new ReadOnlyCollection<string>(new List<string>(selectedGroupIds));
        Steps = new ReadOnlyCollection<TerminalDragStep>(new List<TerminalDragStep>(steps));
        RequestedPieces = requestedPieces;
    }

    /// <summary>Gets the captured visible-page revision.</summary>
    public long ViewRevision { get; }
    /// <summary>Gets the captured presentation-pipeline revision.</summary>
    public long PresentationRevision { get; }
    /// <summary>Gets selected group IDs in final-page slot order.</summary>
    public IReadOnlyList<string> SelectedGroupIds { get; }
    /// <summary>Gets extraction steps in slot order, then group member order.</summary>
    public IReadOnlyList<TerminalDragStep> Steps { get; }
    /// <summary>Gets the total physical pieces requested by this batch.</summary>
    public long RequestedPieces { get; }
}
