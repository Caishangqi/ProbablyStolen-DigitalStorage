using System;

namespace ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

/// <summary>Maintains a temporary physical-piece request for one terminal icon drag.</summary>
public sealed class DragAmountModel
{
    private long _requested;

    /// <summary>Gets whether a quantity gesture is active.</summary>
    public bool IsActive => _requested > 0;
    /// <summary>Gets the current request, or zero when inactive.</summary>
    public long RequestedPieces => _requested;

    /// <summary>Starts a drag at the complete available piece count.</summary>
    public bool Begin(long availablePieces)
    {
        End();
        if (availablePieces <= 0) return false;
        _requested = availablePieces;
        return true;
    }

    /// <summary>Moves the request by whole pieces and clamps it to current availability.</summary>
    public long ApplyWheel(int wholeSteps, long currentAvailablePieces)
    {
        if (!IsActive || currentAvailablePieces <= 0)
        {
            End();
            return 0;
        }

        var limited = Math.Min(_requested, currentAvailablePieces);
        if (wholeSteps > 0)
        {
            var increase = (long)wholeSteps;
            _requested = increase > currentAvailablePieces - limited
                ? currentAvailablePieces
                : limited + increase;
        }
        else if (wholeSteps < 0)
        {
            var decrease = -(long)wholeSteps;
            _requested = decrease >= limited ? 1 : limited - decrease;
        }
        else
        {
            _requested = limited;
        }

        return _requested;
    }

    /// <summary>Halves the request with upward rounding and clamps it to current availability.</summary>
    public long Halve(long currentAvailablePieces)
    {
        if (!IsActive || currentAvailablePieces <= 0)
        {
            End();
            return 0;
        }

        var limited = Math.Min(_requested, currentAvailablePieces);
        _requested = limited / 2 + limited % 2;
        return _requested;
    }

    /// <summary>Clears the temporary request.</summary>
    public void End() => _requested = 0;
}
