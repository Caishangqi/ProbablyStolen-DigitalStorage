using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>A storage cell mounted in a drive.</summary>
public interface IStorageCell : IRootStorage
{
    /// <summary>Gets the current cell state.</summary>
    CellState GetStatus();
}
