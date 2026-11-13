using System;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>Dispatches one back action within an active partition editor.</summary>
public static class PartitionBackHandler
{
    /// <summary>Handles the foremost editor state without affecting the workbench.</summary>
    public static void Handle(bool active, bool menuOpen, bool dragging, bool detailOpen,
        Action closeMenu, Action cancelDrag, Action closeDetail, Action closeEditor)
    {
        if (!active) return;
        if (menuOpen) closeMenu();
        else if (dragging) cancelDrag();
        else if (detailOpen) closeDetail();
        else closeEditor();
    }
}
