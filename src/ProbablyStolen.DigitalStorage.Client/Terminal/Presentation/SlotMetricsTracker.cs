using System;
using ProbablyStolen.DigitalStorage.Api.Client;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>
/// Holds the slot measurements in use and tells subscribers when they change. A subscriber that throws is reported and
/// skipped, so one broken listener never stops the others or the window rebuilds that follow a change.
/// </summary>
public sealed class SlotMetricsTracker : ISlotMetricsProvider
{
    private readonly Action<string> _diagnostic;

    /// <summary>Creates a tracker that starts at <see cref="SlotMetrics.Default"/>.</summary>
    /// <param name="diagnostic">Writes one line when a subscriber throws.</param>
    public SlotMetricsTracker(Action<string> diagnostic) =>
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));

    /// <inheritdoc />
    public SlotMetrics Current { get; private set; } = SlotMetrics.Default;

    /// <inheritdoc />
    public event EventHandler<SlotMetricsChangedEventArgs>? Changed;

    /// <summary>Stores <paramref name="next"/>; raises <see cref="Changed"/> only when it differs from the current value.</summary>
    /// <returns>Whether the measurements changed.</returns>
    public bool Update(SlotMetrics next)
    {
        if (next == null) throw new ArgumentNullException(nameof(next));
        if (next.Equals(Current)) return false;
        var args = new SlotMetricsChangedEventArgs(Current, next);
        Current = next;
        var handlers = Changed;
        if (handlers == null) return true;
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<SlotMetricsChangedEventArgs>)handler)(this, args);
            }
            catch (Exception error)
            {
                _diagnostic($"slot_metrics listener=failed error={error.GetType().Name}");
            }
        }

        return true;
    }
}
