using System;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Platform.Energy;

internal sealed class NativeTransferEnergyReservation : ITransferEnergyReservation
{
    private readonly Func<bool> _refund;
    private bool _settled;

    internal NativeTransferEnergyReservation(Func<bool> refund) =>
        _refund = refund ?? throw new ArgumentNullException(nameof(refund));

    public void Commit() => _settled = true;

    public bool Cancel()
    {
        if (_settled) return false;
        // A failed refund may have reached the native balance. Never call it again.
        _settled = true;
        return _refund();
    }
}
