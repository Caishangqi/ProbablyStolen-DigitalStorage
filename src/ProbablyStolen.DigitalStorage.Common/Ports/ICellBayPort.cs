using ProbablyStolen.DigitalStorage.Common.Contracts;

namespace ProbablyStolen.DigitalStorage.Common.Ports;

/// <summary>Reads one real drive bay without duplicating its physical contents.</summary>
public interface ICellBayPort
{
    /// <summary>Returns the current occupant, or null for an empty bay.</summary>
    CellAssemblySnapshot? Read();
}
