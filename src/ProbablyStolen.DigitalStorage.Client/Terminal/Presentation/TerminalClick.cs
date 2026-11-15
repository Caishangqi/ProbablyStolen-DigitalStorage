using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Terminal slot clicks: left-click, right-click and shift-click extraction.</summary>
public enum TerminalClick
{
    /// <summary>Left click: take one piece.</summary>
    Stack,

    /// <summary>Right click: take half of the slot's pieces.</summary>
    Half,

    /// <summary>Shift + left click: take every piece of the slot.</summary>
    All
}

/// <summary>Maps a slot click to the requested number of pieces.</summary>
public static class TerminalClickAmount
{
    /// <summary>
    /// Gets the pieces to request from a slot holding <paramref name="pieces"/>: left click one, right click half rounded
    /// up, Shift + left click all. A piece is one physical item whatever its unit count, so a request never splits one.
    /// </summary>
    public static long For(TerminalClick click, long pieces)
    {
        if (pieces <= 0) throw new ArgumentOutOfRangeException(nameof(pieces));
        return click switch
        {
            TerminalClick.Stack => 1,
            TerminalClick.Half => (pieces + 1) / 2,
            TerminalClick.All => pieces,
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
