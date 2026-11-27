using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Client;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Draws the overlay images extensions put on slots. Images come from one pool shared by the grid, so a page holds
/// only as many images as it shows overlays; a slot without overlays holds none. Each image covers the slot interior
/// and sits just below the quantity badge, which stays readable on top.
/// </summary>
internal sealed class SlotDecorationLayer
{
    private readonly ItemSlotGrid _grid;
    private readonly Func<string, Sprite?> _overlay;
    private readonly Action<string> _diagnostic;
    private readonly Stack<Image> _pool = new();
    private readonly Dictionary<int, List<Image>> _shown = new();
    private readonly Dictionary<int, IReadOnlyList<SlotDecoration>> _current = new();
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    internal SlotDecorationLayer(ItemSlotGrid grid, Func<string, Sprite?> overlay, Action<string> diagnostic)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    /// <summary>Shows a slot's overlays, bottom first; an unchanged list keeps the images it already has.</summary>
    internal void Set(int index, IReadOnlyList<SlotDecoration> decorations)
    {
        if (decorations == null) throw new ArgumentNullException(nameof(decorations));
        if (_current.TryGetValue(index, out var current) ? Same(current, decorations) : decorations.Count == 0) return;
        Release(index);
        if (decorations.Count == 0) return;
        var parent = _grid.OverlayParent(index, out var sibling);
        if (parent == null) return;
        var images = new List<Image>(decorations.Count);
        foreach (var decoration in decorations)
        {
            var sprite = _overlay(decoration.OverlayKey);
            if (sprite == null)
            {
                if (_reported.Add(decoration.OverlayKey))
                    _diagnostic($"slot_overlay missing key={decoration.OverlayKey}");
                continue;
            }

            var image = Take();
            var rect = (RectTransform)image.transform;
            rect.SetParent(parent, false);
            rect.SetSiblingIndex(sibling++);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(SlotMetrics.BorderPadding, SlotMetrics.BorderPadding);
            rect.offsetMax = new Vector2(-SlotMetrics.BorderPadding, -SlotMetrics.BorderPadding);
            image.sprite = sprite;
            // Overlays are grayscale; the tint multiplies them, the same way the game colors palette text.
            image.color = RenderHandler.GetColorFromPalette((RenderHandler.ColorPalette)decoration.TintRgb);
            image.gameObject.SetActive(true);
            images.Add(image);
        }

        _shown[index] = images;
        _current[index] = decorations;
    }

    internal void Clear()
    {
        foreach (var images in _shown.Values) Recycle(images);
        _shown.Clear();
        _current.Clear();
    }

    /// <summary>Drops every reference after the window closes; the images were destroyed with it.</summary>
    internal void Reset()
    {
        _shown.Clear();
        _current.Clear();
        _pool.Clear();
    }

    private void Release(int index)
    {
        _current.Remove(index);
        if (!_shown.TryGetValue(index, out var images)) return;
        _shown.Remove(index);
        Recycle(images);
    }

    private void Recycle(List<Image> images)
    {
        foreach (var image in images)
        {
            if (image == null) continue;
            image.gameObject.SetActive(false);
            _pool.Push(image);
        }
    }

    private Image Take()
    {
        while (_pool.Count > 0)
        {
            var pooled = _pool.Pop();
            if (pooled != null) return pooled;
        }

        var image = new GameObject("terminal_slot_overlay", typeof(RectTransform)).AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = false;
        image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return image;
    }

    private static bool Same(IReadOnlyList<SlotDecoration> left, IReadOnlyList<SlotDecoration> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (left[i].OverlayKey != right[i].OverlayKey || left[i].TintRgb != right[i].TintRgb) return false;
        return true;
    }
}
