using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// The one-line tooltip shown while items are dragged over a terminal: green when a single-item preview accepts,
/// red when it refuses, and neutral when a group will be checked after release.
/// </summary>
internal static class TerminalDropHint
{
    /// <summary>Builds the hint; pending feedback makes no claim about capacity.</summary>
    internal static RichText Build(ModText text, int count, GameItem? refused, string? refusal, bool pending = false)
    {
        var target = "<u>" + text.Get("terminal_name") + "</u>";
        var builder = new RichTextBuilder(60);
        if (pending)
        {
            builder.Add(text.Get("terminal_storage_drag_pending", count, target), RenderHandler.ColorPalette.LightGray);
        }
        else if (refusal == null)
        {
            builder.Add(text.Get("terminal_storage_drag_hint", count, target), RenderHandler.ColorPalette.LightGreen);
        }
        else
        {
            builder.Add(text.Get("terminal_storage_drag_refused", refused?.GetDisplayName(preferCustomName: true) ?? string.Empty,
                target, text.Get(DropRefusalReasons.ReasonKey(refusal))), RenderHandler.ColorPalette.Red);
        }

        return builder.Build();
    }
}
