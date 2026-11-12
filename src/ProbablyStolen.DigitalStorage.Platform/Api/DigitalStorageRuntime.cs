using System;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Networking;
using ProbablyStolen.DigitalStorage.Api.Storage;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Api;

/// <summary>The public API entry of one mod lifecycle; created by the platform composition.</summary>
internal sealed class DigitalStorageRuntime : IDigitalStorageRuntime
{
    private readonly ExternalAccessScope _access;
    private readonly GridHelper _grids;

    /// <param name="networkPower">Network scanning, power and terminal storage operations.</param>
    /// <param name="scanner">The network scanner.</param>
    /// <param name="log">Writes a warning.</param>
    /// <param name="contentsChanged">Tells open views that stored items moved through the API.</param>
    internal DigitalStorageRuntime(GridNetworkPowerCoordinator networkPower, NativeNetworkScanner scanner, Action<string> log,
        Action contentsChanged)
    {
        if (networkPower == null) throw new ArgumentNullException(nameof(networkPower));
        if (scanner == null) throw new ArgumentNullException(nameof(scanner));
        if (log == null) throw new ArgumentNullException(nameof(log));
        if (contentsChanged == null) throw new ArgumentNullException(nameof(contentsChanged));

        var thread = new ApiThread();
        _access = new ExternalAccessScope(root => CellPages.CellOfStored(root, out _), IsCellOnline, CellPages.Compact,
            () => UnityEngine.Time.frameCount, () => thread.IsMain, log);
        _grids = new GridHelper(scanner, networkPower.IsPowered,
            machine => new NetworkStorageFacade(networkPower.Storage, machine, _access, thread, log, contentsChanged));
    }

    /// <inheritdoc />
    public IGridHelper Grids => _grids;

    /// <inheritdoc />
    public IExternalAccess ExternalAccess => _access;

    // A cell is reachable when it sits in a drive bay of an online network and its pages can be read.
    private bool IsCellOnline(GameItem cell)
    {
        var drive = cell.parentInventory?.GetParentItem();
        return drive != null && drive.identifier == DriveItem.ItemId && CellHealth.Get(cell).IsMountable &&
            _grids.TryGetNode(drive, out var node) && node!.IsOnline;
    }
}
