namespace ProbablyStolen.DigitalStorage.Client.Tooltips;

/// <summary>Pointer following moves an already requested hint; only a new content request can restore it after hiding.</summary>
internal sealed class TooltipVisibilityState
{
    internal bool CanFollow { get; private set; }
    internal void Show() => CanFollow = true;
    internal void Hide() => CanFollow = false;
}
