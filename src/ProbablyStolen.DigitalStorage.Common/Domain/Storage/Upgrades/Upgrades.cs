namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

/// <summary>Installation limits for each cell module kind.</summary>
public static class Upgrades
{
    /// <summary>Gets the most modules of a kind one cell may hold, or 0 for an unknown kind.</summary>
    /// <remarks>Type limit modules have no count limit of their own; the module bay's shape bounds them.</remarks>
    public static int GetMaxInstallable(CellModuleKind kind) => kind switch
    {
        CellModuleKind.TypeLimit => int.MaxValue,
        CellModuleKind.EqualDistribution => 1,
        _ => 0
    };
}
