using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>Lays the stack mark, tinted with the terminal's marker color, over a slot that merges several entries.</summary>
public sealed class MixedGroupDecorator : ISlotDecorator
{
    /// <summary>The decorator id.</summary>
    public const string DecoratorId = "stacking_mixed";

    /// <summary>The overlay image of the mark.</summary>
    public const string OverlayKey = "annotation_square";

    /// <inheritdoc />
    public string Id => DecoratorId;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsActive(TerminalContext context) => StackingModule.IsInstalled(context);

    /// <inheritdoc />
    public void Decorate(DisplayEntry entry, TerminalContext context, SlotDecorationSink sink)
    {
        if (!entry.IsMixed) return;
        var marker = StackingProjector.StoreOf(context.FeatureData).Effective()?.Marker ?? StackingMarker.Default;
        sink.Add(OverlayKey, marker.Rgb);
    }
}
