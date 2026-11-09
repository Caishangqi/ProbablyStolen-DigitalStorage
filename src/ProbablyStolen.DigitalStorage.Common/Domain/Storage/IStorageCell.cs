namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>A storage cell mounted in a drive, as seen by grid status checks.</summary>
public interface IStorageCell
{
    /// <summary>Gets the current cell state.</summary>
    CellState GetStatus();
}
