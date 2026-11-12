namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

/// <summary>Module kinds accepted by a storage cell.</summary>
public enum CellModuleKind
{
    /// <summary>Raises the effective type limit by a rolled amount and percentage.</summary>
    TypeLimit = 0,

    /// <summary>Enables equal distribution.</summary>
    EqualDistribution = 1
}
