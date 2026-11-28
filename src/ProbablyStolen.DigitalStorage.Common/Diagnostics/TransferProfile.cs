using System.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Common.Diagnostics;

/// <summary>A measured part of moving one root. Parts can nest; each reports its own inclusive time.</summary>
internal enum TransferPart
{
    Identify,
    IdentifyKept,
    PlacementCheck,
    NativeMove,
    LayoutRead,
    SourcePeek,
    StorePlan,
    NetworkInsert,
    PartitionCheck,
    ViewRecord,
    CellInsert,
    SourceExtract,
    Count
}

/// <summary>
/// Splits the time of IO Port work into the parts of a root's move, so the batch timing log shows which part
/// dominates. Counts only while a session step runs; costs one timestamp per part otherwise skipped.
/// </summary>
internal static class TransferProfile
{
    private static readonly long[] Ticks = new long[(int)TransferPart.Count];
    private static readonly int[] Calls = new int[(int)TransferPart.Count];

    internal static bool Active { get; private set; }

    internal static void Reset()
    {
        System.Array.Clear(Ticks, 0, Ticks.Length);
        System.Array.Clear(Calls, 0, Calls.Length);
    }

    internal static void Resume() => Active = true;

    internal static void Pause() => Active = false;

    internal static long Mark() => Active ? Stopwatch.GetTimestamp() : 0;

    internal static void Add(TransferPart part, long mark)
    {
        if (!Active || mark == 0) return;
        Ticks[(int)part] += Stopwatch.GetTimestamp() - mark;
        Calls[(int)part]++;
    }

    internal static long TicksOf(TransferPart part) => Ticks[(int)part];

    internal static int CallsOf(TransferPart part) => Calls[(int)part];
}
