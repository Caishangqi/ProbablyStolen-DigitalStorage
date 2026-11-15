using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>One member and the pieces to ask of it.</summary>
public readonly struct GroupExtractionStep
{
    internal GroupExtractionStep(TerminalEntry member, long pieces)
    {
        Member = member;
        Pieces = pieces;
    }

    /// <summary>Gets the member.</summary>
    public TerminalEntry Member { get; }

    /// <summary>Gets the pieces to take from it.</summary>
    public long Pieces { get; }
}

/// <summary>Splits a request over a group's members in group order, never asking a member for more than it holds.</summary>
public static class GroupExtractionPlan
{
    /// <summary>Returns the steps that take <paramref name="requested"/> pieces, or fewer when the group holds fewer.</summary>
    public static IReadOnlyList<GroupExtractionStep> Steps(DisplayEntry group, long requested)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (requested <= 0) throw new ArgumentOutOfRangeException(nameof(requested));
        var steps = new List<GroupExtractionStep>();
        var remaining = requested;
        foreach (var member in group.Members)
        {
            if (remaining == 0) break;
            if (member.Amount <= 0) continue;
            var pieces = Math.Min(remaining, member.Amount);
            steps.Add(new GroupExtractionStep(member, pieces));
            remaining -= pieces;
        }

        return steps;
    }
}
