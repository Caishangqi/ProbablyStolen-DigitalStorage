using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Terminal slot clicks, after AE2's left-click, right-click and shift-click extraction.</summary>
public enum TerminalClick
{
    /// <summary>Left click: take up to one full stack.</summary>
    Stack,

    /// <summary>Right click: take half of one full stack.</summary>
    Half,

    /// <summary>Shift + left click: take everything of this type.</summary>
    All
}

/// <summary>Maps a slot click to the requested extraction amount.</summary>
public static class TerminalClickAmount
{
    /// <summary>
    /// Gets the amount to request. A full stack is the item's native stack limit or everything stored, whichever is
    /// smaller; half rounds up so a single unit can still be taken.
    /// </summary>
    public static long For(TerminalClick click, long stored, long stackLimit)
    {
        if (stored <= 0) throw new ArgumentOutOfRangeException(nameof(stored));
        var stack = Math.Min(stored, Math.Max(1, stackLimit));
        return click switch
        {
            TerminalClick.Stack => stack,
            TerminalClick.Half => (stack + 1) / 2,
            TerminalClick.All => stored,
            _ => throw new ArgumentOutOfRangeException(nameof(click))
        };
    }
}

/// <summary>Chooses which stored stacks an extraction moves; stored stacks are never split.</summary>
public static class TerminalExtractionPlan
{
    /// <summary>
    /// Returns stack indices in stored order. The first stack is always taken; after that a stack is skipped when it
    /// would carry the total past <paramref name="amount"/>, and selection stops once the amount is reached.
    /// </summary>
    public static IReadOnlyList<int> Select(IReadOnlyList<long> stackUnits, long amount)
    {
        if (stackUnits == null) throw new ArgumentNullException(nameof(stackUnits));
        var selected = new List<int>();
        long total = 0;
        for (var index = 0; index < stackUnits.Count && total < amount; index++)
        {
            var units = stackUnits[index];
            if (units <= 0 || selected.Count > 0 && total + units > amount) continue;
            selected.Add(index);
            total += units;
        }

        return selected;
    }
}
