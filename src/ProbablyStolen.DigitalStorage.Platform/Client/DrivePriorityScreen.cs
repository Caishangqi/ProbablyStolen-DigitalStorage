using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Drive;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// The drive's priority page: shows the drive's native priority panel, borrowed from its native window like the cell
/// bays, so the page has the game's own titled frame. The panel goes back when the page closes.
/// </summary>
internal sealed class DrivePriorityScreen : SubScreen
{
    private readonly MachineryPanelHost _host;

    internal DrivePriorityScreen(DriveWindow window) : base(window)
    {
        _host = new MachineryPanelHost(window.WriteDiagnostic, "Digital Storage Drive Priority Host", "drive_priority_embed",
            "drive_priority_restore");
    }

    /// <inheritdoc />
    public override string ScreenId => DriveTerminalFeature.PriorityScreenId;

    internal override bool FitsContent => true;

    protected override bool BuildContent(CustomUIBuilder builder)
    {
        var drive = Window.Terminal;
        if (DrivePriorityPanel.Of(drive) is not { } priority ||
            drive.contentWindow?.childElement is not MachineryPanelRow row || row.GetPanel(DrivePriorityPanel.Role) is not { } panel)
        {
            Window.WriteDiagnostic("drive_priority result=missing");
            return false;
        }

        MachineryContainerUtils.TryResize(panel.content, 0, 0);
        panel.ResizePixels(0, 0);
        panel.Validate();
        var scale = 1f / TerminalWindowMetrics.NativeScale;
        var holder = new GameObject(DrivePriorityPanel.Role, typeof(RectTransform)).GetComponent<RectTransform>();
        holder.SetParent(builder.CurrentContainer, false);
        var layout = holder.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = panel.widthPixels * scale;
        layout.minHeight = layout.preferredHeight = panel.heightPixels * scale;
        if (!_host.Show(panel, row, holder, scale)) return false;
        priority.Refresh();
        return true;
    }

    internal override void Dispose()
    {
        _host.Dispose("drive_priority_closed");
        base.Dispose();
    }
}
