using System;
using ProbablyStolen.DigitalStorage.Api.Content;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>Coordinates a single module bay's reversible changes and material consumption.</summary>
public sealed class ModuleBayTransaction
{
    private bool _active;
    private bool _unavailable;
    private bool _consumptionUncertain;

    /// <summary>Gets whether a failed recovery or uncertain consumption prevents further changes.</summary>
    public bool IsLocked => _unavailable || _consumptionUncertain;

    /// <summary>Gets whether the outcome of material consumption is unknown.</summary>
    public bool ConsumptionUncertain => _consumptionUncertain;

    /// <summary>Applies a transition. The port owns its snapshot and must restore it exactly on request.</summary>
    public ModuleBayChangeResult Execute(IModuleBayTransactionPort port, int stage, int maximumStage,
        ModuleBayLayout nextLayout, bool consumeMaterial)
    {
        if (port == null) throw new ArgumentNullException(nameof(port));
        if (nextLayout == null) throw new ArgumentNullException(nameof(nextLayout));
        if (stage < 0 || maximumStage < 0 || stage > maximumStage)
            return ModuleBayChangeResult.Failed(ModuleBayChangeStatus.Invalid, Math.Max(stage, 0), "invalid stage");
        if (IsLocked || _active)
            return ModuleBayChangeResult.Failed(ModuleBayChangeStatus.Unavailable, stage, "module bay locked or busy");
        if (stage == maximumStage) return ModuleBayChangeResult.Refused(stage, "maximum stage");

        _active = true;
        try
        {
            try { port.Capture(); }
            catch { return ModuleBayChangeResult.Failed(ModuleBayChangeStatus.Unavailable, stage, "snapshot failed"); }
            try
            {
                port.Apply(nextLayout, stage + 1);
                port.Validate();
                if (consumeMaterial) port.AddValue();
            }
            catch
            {
                try { port.Restore(); }
                catch { _unavailable = true; }
                return ModuleBayChangeResult.Failed(ModuleBayChangeStatus.Unavailable, stage,
                    _unavailable ? "rollback failed" : "change failed");
            }

            if (consumeMaterial)
            {
                try { port.Consume(); }
                catch
                {
                    _consumptionUncertain = true;
                    try { port.Notify(); }
                    catch { }
                    return ModuleBayChangeResult.Failed(ModuleBayChangeStatus.ConsumptionUncertain, stage + 1,
                        "material consumption uncertain");
                }
            }
            try { port.Notify(); }
            catch { }
            return ModuleBayChangeResult.Changed(stage + 1);
        }
        finally { _active = false; }
    }
}

/// <summary>Per-attempt platform operations. Capture and restore include the entire mutable native state.</summary>
public interface IModuleBayTransactionPort
{
    /// <summary>Captures the state before mutation without changing it.</summary>
    void Capture();
    /// <summary>Applies the requested layout and stage.</summary>
    void Apply(ModuleBayLayout layout, int stage);
    /// <summary>Validates the changed native inventory.</summary>
    void Validate();
    /// <summary>Adds the native expansion value.</summary>
    void AddValue();
    /// <summary>Restores the captured state exactly.</summary>
    void Restore();
    /// <summary>Invokes native material consumption once.</summary>
    void Consume();
    /// <summary>Notifies observers after a successful commit.</summary>
    void Notify();
}
