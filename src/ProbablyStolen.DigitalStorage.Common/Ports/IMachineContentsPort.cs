namespace ProbablyStolen.DigitalStorage.Common.Ports;

/// <summary>Restores the existing native machine window's module configuration.</summary>
public interface IMachineContentsPort
{
    /// <summary>Updates the existing module grid and locks without replacing the window or its inventories.</summary>
    void RestoreModules();
}
