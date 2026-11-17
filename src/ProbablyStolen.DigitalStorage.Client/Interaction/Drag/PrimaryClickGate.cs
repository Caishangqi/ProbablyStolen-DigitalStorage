namespace ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

/// <summary>Keeps a cancelled or consumed pointer press from reviving as a later ordinary click.</summary>
public sealed class PrimaryClickGate
{
    private bool _blockedUntilNextPress;

    /// <summary>Starts a newly accepted primary press, discarding suppression from an older gesture.</summary>
    public void BeginPress() => _blockedUntilNextPress = false;

    /// <summary>Invalidates a pending press until a fresh primary press is accepted.</summary>
    public void Invalidate() => _blockedUntilNextPress = true;

    /// <summary>Decides native release consumption using eligibility captured before the release callback.</summary>
    public bool CompleteRelease(bool allowedBeforeRelease, bool releaseConsumed)
    {
        if (!allowedBeforeRelease || releaseConsumed) _blockedUntilNextPress = true;
        return _blockedUntilNextPress;
    }

    /// <summary>Checks both current ownership and suppression retained after release or cancellation.</summary>
    public bool SuppressesClick(bool gestureOwned, bool allowsPrimaryClick, bool consumedReleaseFrame) =>
        _blockedUntilNextPress || consumedReleaseFrame || gestureOwned && !allowsPrimaryClick;
}
