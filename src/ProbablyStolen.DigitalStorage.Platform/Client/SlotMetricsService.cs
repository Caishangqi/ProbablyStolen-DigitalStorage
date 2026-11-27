using System;
using ProbablyStolen.DigitalStorage.Api.Client;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// The slot measurements of every terminal-style window and of the public API. They follow the player's three settings
/// and the game viewport, which has no change notification, so windows refresh them each time they are built.
/// </summary>
internal sealed class SlotMetricsService : ISlotMetricsProvider
{
    private readonly ITerminalViewSettings _settings;
    private readonly SlotMetricsTracker _tracker;

    internal SlotMetricsService(ITerminalViewSettings settings, Action<string> diagnostic)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _tracker = new SlotMetricsTracker(diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)));
    }

    /// <inheritdoc />
    public SlotMetrics Current => _tracker.Current;

    /// <inheritdoc />
    public event EventHandler<SlotMetricsChangedEventArgs>? Changed
    {
        add => _tracker.Changed += value;
        remove => _tracker.Changed -= value;
    }

    /// <summary>
    /// Recomputes the measurements from the settings and the viewport; open windows whose slots differ rebuild through
    /// <see cref="Changed"/>. Main thread only.
    /// </summary>
    internal SlotMetrics Refresh()
    {
        var canvas = RenderHandler.current?.windowRoot?.rectTransform.GetComponentInParent<Canvas>();
        _tracker.Update(TerminalWindowMetrics.Resolve(_settings.SlotSize, _settings.IconMargin, _settings.DockButtonSize,
            LetterboxManager.GameAreaWidth, LetterboxManager.GameAreaHeight, canvas == null ? 0f : canvas.scaleFactor));
        return _tracker.Current;
    }

    /// <summary>Stores a new slot size and applies it.</summary>
    internal void SetSlotSize(SizeTier tier)
    {
        _settings.SlotSize = tier;
        Refresh();
    }

    /// <summary>Stores a new dock button size and applies it.</summary>
    internal void SetDockButtonSize(SizeTier tier)
    {
        _settings.DockButtonSize = tier;
        Refresh();
    }

    /// <summary>Stores a new icon margin and applies it.</summary>
    internal void SetIconMargin(IconMarginTier margin)
    {
        _settings.IconMargin = margin;
        Refresh();
    }
}
