namespace ProbablyStolen.DigitalStorage.Common.Ports;

/// <summary>Reads and atomically consumes one controller-owned native energy source.</summary>
public interface IEnergySourcePort
{
    /// <summary>Whether the controller currently contains a valid power source item.</summary>
    bool HasSource { get; }

    /// <summary>Checks whether the source can supply the complete requested amount without mutation.</summary>
    bool CanDraw(int amount);

    /// <summary>Draws the whole requested amount or leaves the source unchanged.</summary>
    bool TryDraw(int amount);
}
