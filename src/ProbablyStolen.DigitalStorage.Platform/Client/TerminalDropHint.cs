using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// The one-line tooltip shown while items are dragged over a terminal, in the shape of the game's own drag tooltips:
/// the target underlined, green when the drop would store and red with a short reason when it would not.
/// </summary>
internal static class TerminalDropHint
{
    /// <summary>Builds the hint; a null <paramref name="refusal"/> means the drop would store.</summary>
    internal static RichText Build(ModText text, int count, GameItem? refused, string? refusal)
    {
        var target = "<u>" + text.Get("terminal_name") + "</u>";
        var builder = new RichTextBuilder(60);
        if (refusal == null)
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
