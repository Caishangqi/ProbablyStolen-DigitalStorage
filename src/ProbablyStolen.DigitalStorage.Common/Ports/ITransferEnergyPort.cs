using System;
using System.Diagnostics.CodeAnalysis;

namespace ProbablyStolen.DigitalStorage.Common.Ports;

/// <summary>Reserves a metered operation charge on the controller's current source.</summary>
/// <remarks>Use synchronously on the game thread after checking that the source covers the base demand and the charge.</remarks>
public interface ITransferEnergyPort
{
    /// <summary>Reserves a positive amount and returns a handle for confirming or undoing the debit.</summary>
    /// <returns>False for a confirmed refusal with no debit and a null handle.</returns>
    /// <exception cref="InvalidOperationException">The debit could not be verified. End this attempt before moving the root.</exception>
    bool TryReserve(int amount, [NotNullWhen(true)] out ITransferEnergyReservation? reservation);
}
