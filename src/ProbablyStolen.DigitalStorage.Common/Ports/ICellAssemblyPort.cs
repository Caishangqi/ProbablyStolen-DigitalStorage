using ProbablyStolen.DigitalStorage.Common.Contracts;

namespace ProbablyStolen.DigitalStorage.Common.Ports;

/// <summary>Accesses one native host graph. Implementations retain the original host and child.</summary>
public interface ICellAssemblyPort
{
    /// <summary>Reads the current physical graph rather than a cached inventory.</summary>
    CellAssemblySnapshot Read();

    /// <summary>Fixes the already attached component without creating or moving an item.</summary>
    void FixComponent();

    /// <summary>Changes the original host's definition and appearance without replacing its slot graph.</summary>
    void Present(bool assembled);
}
