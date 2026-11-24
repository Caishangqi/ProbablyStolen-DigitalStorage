namespace ProbablyStolen.DigitalStorage.Common.Ports;

/// <summary>Settles one operation charge synchronously on the game thread.</summary>
/// <remarks>Leave the reservation as it stands if root ownership is unresolved. Report the failure for this attempt.
/// A later callback may work from a fresh plan without settling this reservation again.</remarks>
public interface ITransferEnergyReservation
{
    /// <summary>Keeps the debit after a verified transfer. Call once per reservation.
    /// This local confirmation of a valid reservation must succeed without further source changes.</summary>
    void Commit();

    /// <summary>Restores only this reservation's debit after a confirmed refusal or verified return to the source.</summary>
    /// <returns>True if the restoration is confirmed. False leaves the refund unverified for the current attempt.</returns>
    bool Cancel();
}
