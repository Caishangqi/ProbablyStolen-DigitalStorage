using System;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>Restores one machine's existing contents once, on its first open or idle callback after decoding.</summary>
public sealed class MachineContentLifecycle
{
    private readonly IMachineContentsPort _port;
    private bool _restored;

    /// <summary>Binds the lifecycle to one machine, without owning or releasing its inventories.</summary>
    public MachineContentLifecycle(IMachineContentsPort port)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
    }

    /// <summary>Repeated opens retain the existing contents. Failed restoration can be retried on the same graph.</summary>
    public void RestoreIfReady(bool decoding)
    {
        if (_restored || decoding)
        {
            return;
        }

        _port.RestoreModules();
        _restored = true;
    }
}
