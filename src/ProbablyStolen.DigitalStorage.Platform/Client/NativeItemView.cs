using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Mirrors the item picture the game draws for a stored item into plain UI images.
/// <c>GameItemElement.ValidateSelect</c> stacks the item sprite (<c>background</c>, possibly an animation frame), the
/// sprite modifiers of child items and the item's own modifiers (for example the crop of a working hydroponic
/// machine) as direct children of <c>rotator</c>. Copying those finished layers shows whatever the game or other mods
/// drew. The copies are ordinary images: no native components and no raycasts.
/// </summary>
internal static class NativeItemView
{
    /// <summary>
    /// Asks the game to redraw the item's layers. Storage pages place items with UncheckedAccept, which only runs
    /// ValidateShapeState, so modifier layers (such as crops) are never built for a stored or reloaded item until this
    /// native redraw runs. The background toggle is left untouched.
    /// </summary>
    internal static void Redraw(GameItemElement item) => item.ValidateSelect(updateBackground: false);

    /// <summary>Copies the item's current layers into <paramref name="container"/>, fitted and centred. Returns false when nothing could be copied.</summary>
    internal static bool Sync(RectTransform container, List<Image> pool, GameItemElement item) =>
        Sync(container, pool, Capture(item));

    /// <summary>Captures finished layers without retaining the native item or its image components.</summary>
    internal static Picture Capture(GameItemElement item)
    {
        var rotator = item.rotator?.rectTransform;
        if (rotator == null)
        {
            return new Picture(new List<Layer>());
        }

        return new Picture(Collect(rotator, item.inner?.rectTransform));
    }

    /// <summary>Renders a captured picture without accessing native items.</summary>
    internal static bool Sync(RectTransform container, List<Image> pool, Picture picture)
    {
        var layers = picture.Layers;
        if (layers.Count == 0)
        {
            Hide(pool, 0);
            return false;
        }

        // Fit the union of the layers, not the rotator rect: native sprites may extend beyond the item's grid size.
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var layer in layers)
        {
            foreach (var corner in layer.Corners())
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }
        }

        var extent = max - min;
        if (extent.x <= 0f || extent.y <= 0f)
        {
            Hide(pool, 0);
            return false;
        }

        // A frame keeps the item's aspect ratio inside the slot. Layer positions use fractions of that frame
        // because the slot's unit scale under the reparented terminal window is not known here.
        var frame = Frame(container);
        frame.GetComponent<AspectRatioFitter>().aspectRatio = extent.x / extent.y;
        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            var copy = Take(frame, pool, index);
            copy.sprite = layer.Sprite;
            // Tiled drawing ignores the reduced rect and clips the sprite, so copies stretch the whole sprite.
            copy.type = Image.Type.Simple;
            copy.color = layer.Color;
            copy.preserveAspect = false;
            var rect = copy.rectTransform;
            var half = layer.Size * 0.5f;
            rect.anchorMin = Divide(layer.Centre - half - min, extent);
            rect.anchorMax = Divide(layer.Centre + half - min, extent);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localRotation = layer.Rotation;
            rect.SetSiblingIndex(index);
            copy.enabled = true;
        }

        Hide(pool, layers.Count);
        return true;
    }

    /// <summary>Hides every mirrored layer.</summary>
    internal static void Clear(List<Image> pool) => Hide(pool, 0);

    internal static string LastSummary { get; private set; } = "none";

    private static List<Layer> Collect(RectTransform rotator, RectTransform? inner)
    {
        var layers = new List<Layer>();
        for (var index = 0; index < rotator.childCount; index++)
        {
            var child = rotator.GetChild(index) as RectTransform;
            // inner is where child elements render; only the item's own picture layers are mirrored. Use activeSelf:
            // a stored item's cell window is closed, so its whole render tree is inactive in the hierarchy, while
            // the game toggles each layer itself with SetActive / image.enabled (GameItemElement.UpdateInnateModSprite).
            if (child == null || child == inner || !child.gameObject.activeSelf) continue;
            var source = child.GetComponent<Image>();
            if (source == null || !source.enabled || source.sprite == null || source.color.a <= 0f) continue;

            // Layers are direct rotator children, so their local transforms already are rotator space (which also
            // drops the stored rotation: the terminal shows the upright item). World transforms are avoided because
            // the closed cell window's render chain does not carry the normal screen scale.
            var localScale = child.localScale;
            var centre = (Vector2)(child.localPosition +
                child.localRotation * Vector3.Scale(child.rect.center, localScale));
            var size = new Vector2(child.rect.width * Mathf.Abs(localScale.x), child.rect.height * Mathf.Abs(localScale.y));
            layers.Add(new Layer(source.sprite, source.color, centre, size, child.localRotation));
        }

        return layers;
    }

    private static Vector2 Divide(Vector2 value, Vector2 by) => new(value.x / by.x, value.y / by.y);

    /// <summary>Gets the frame that preserves aspect ratio inside a slot, creating it on first use.</summary>
    private static RectTransform Frame(RectTransform container)
    {
        if (container.childCount > 0 && container.GetChild(0) is RectTransform existing &&
            existing.GetComponent<AspectRatioFitter>() != null)
        {
            return existing;
        }

        var frame = (RectTransform)new GameObject("terminal_item_frame", typeof(RectTransform)).transform;
        frame.SetParent(container, false);
        frame.SetAsFirstSibling();
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.pivot = new Vector2(0.5f, 0.5f);
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        frame.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        return frame;
    }

    private static Image Take(RectTransform container, List<Image> pool, int index)
    {
        if (index < pool.Count) return pool[index];
        // No RectMask2D: the layers are already fitted inside the slot, and under the terminal's reparented, scaled
        // window a RectMask2D clipped the fitted copies at a misplaced rectangle.
        var image = new GameObject("terminal_item_layer", typeof(RectTransform)).AddComponent<Image>();
        image.transform.SetParent(container, false);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        image.raycastTarget = false;
        pool.Add(image);
        return image;
    }

    private static void Hide(List<Image> pool, int from)
    {
        for (var index = from; index < pool.Count; index++)
        {
            if (pool[index] != null) pool[index].enabled = false;
        }
    }

    internal sealed class Picture
    {
        internal Picture(List<Layer> layers) => Layers = layers.AsReadOnly();
        internal IReadOnlyList<Layer> Layers { get; }
    }

    internal readonly struct Layer
    {
        internal Layer(Sprite sprite, Color color, Vector2 centre, Vector2 size, Quaternion rotation)
        {
            Sprite = sprite;
            Color = color;
            Centre = centre;
            Size = size;
            Rotation = rotation;
        }

        internal Sprite Sprite { get; }

        internal Color Color { get; }

        internal Vector2 Centre { get; }

        internal Vector2 Size { get; }

        internal Quaternion Rotation { get; }

        internal IEnumerable<Vector2> Corners()
        {
            var half = Size * 0.5f;
            foreach (var sign in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            {
                yield return Centre + (Vector2)(Rotation * Vector2.Scale(half, sign));
            }
        }
    }
}
