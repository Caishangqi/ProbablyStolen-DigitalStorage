namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Native events of the partition editor that the IMod assembly's Harmony patches report.</summary>
public static class PartitionEditorInput
{
    /// <summary>Reports a native window that was hidden; the editor closes with its workbench window.</summary>
    public static void WindowHidden(PixelWindow window) => PartitionWindow.OnWindowHidden(window);

    /// <summary>Reports a native drag that started; the editor closes when its cell is picked up.</summary>
    public static void DragStarted(ItemMouseDragHandler drag) => PartitionWindow.OnDragStarted(drag);
}
