namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>Usage for module removal, including whether the layout itself is unavailable.</summary>
internal readonly struct CellModuleUsage
{
    internal enum ReadStatus { Pending, Ready, Unavailable }

    private CellModuleUsage(ReadStatus status, int storedTypes, long areaUnits, bool layoutUnavailable)
    {
        Status = status;
        StoredTypes = storedTypes;
        AreaUnits = areaUnits;
        LayoutUnavailable = layoutUnavailable;
    }

    internal ReadStatus Status { get; }
    internal int StoredTypes { get; }
    internal long AreaUnits { get; }
    internal bool LayoutUnavailable { get; }

    internal static CellModuleUsage Ready(int storedTypes, long areaUnits) => new(ReadStatus.Ready, storedTypes, areaUnits, false);

    internal static CellModuleUsage Unavailable(bool layoutUnavailable) => new(ReadStatus.Unavailable, 0, 0, layoutUnavailable);
}
