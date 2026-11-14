using System;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>Restores bound rule hints after a redraw or drag without requiring another pointer-enter event.</summary>
public sealed class PartitionHoverHintCoordinator
{
    private readonly Action<int> _show;
    private readonly Action _hide;
    private int _slot = -1;
    private string? _text;

    /// <summary>Creates a coordinator whose callbacks show a slot's bound hint and hide only this grid's hints.</summary>
    public PartitionHoverHintCoordinator(Action<int> show, Action hide)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
    }

    /// <summary>Shows the current bound text once per hover or content change, while the overview can receive input.</summary>
    public void Refresh(int slot, string? text, bool canShow)
    {
        if (!canShow || slot < 0 || string.IsNullOrEmpty(text))
        {
            Hide();
            return;
        }

        if (_slot == slot && string.Equals(_text, text, StringComparison.Ordinal)) return;
        _show(slot);
        _slot = slot;
        _text = text;
    }

    /// <summary>Hides and invalidates the hint, so the same stationary pointer can show it after the transition.</summary>
    public void Hide()
    {
        // A native pointer-enter can show a bound hint before this coordinator has observed the hover.
        _hide();
        _slot = -1;
        _text = null;
    }
}
