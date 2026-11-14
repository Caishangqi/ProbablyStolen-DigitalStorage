using System;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>Routes a partition redraw to the visible layer while keeping the same draft owner.</summary>
public static class PartitionViewRefreshCoordinator
{
    /// <summary>Clamps the scroll offset after a detail rebuild changes the content height.</summary>
    public static float ClampScrollOffset(float offset, float contentHeight, float viewportHeight)
    {
        if (float.IsNaN(offset) || float.IsInfinity(offset) || float.IsNaN(contentHeight) ||
            float.IsInfinity(contentHeight) || float.IsNaN(viewportHeight) || float.IsInfinity(viewportHeight))
            return 0f;
        var maximum = Math.Max(0f, contentHeight - Math.Max(0f, viewportHeight));
        return Math.Max(0f, Math.Min(offset, maximum));
    }

    /// <summary>Rebuilds the detail screen when it is the active child page, otherwise the overview.</summary>
    public static void Refresh(bool hasDetailDraft, bool detailScreenVisible, Action refreshOverview,
        Action refreshDetailScreen)
    {
        if (refreshOverview == null) throw new ArgumentNullException(nameof(refreshOverview));
        if (refreshDetailScreen == null) throw new ArgumentNullException(nameof(refreshDetailScreen));
        if (hasDetailDraft && detailScreenVisible) refreshDetailScreen();
        else refreshOverview();
    }
}
