using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

// The machinery atlas has no arrows. Generated white sprites use point filtering and the image's tint.
internal static class PixelArrow
{
    internal enum Direction
    {
        Down,
        Left,
        Right
    }

    // One pixel of the sprite is two layout units, as the machinery sprites are drawn (pixelsPerUnitMultiplier 0.5).
    internal const float UnitsPerPixel = 2f;
    private static readonly Dictionary<Direction, Sprite> Sprites = new();

    internal static Sprite Get(Direction direction)
    {
        if (Sprites.TryGetValue(direction, out var cached) && cached != null) return cached;
        var down = direction == Direction.Down;
        var texture = new Texture2D(down ? 7 : 4, down ? 4 : 7, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "digital_storage_arrow_" + direction
        };
        var clear = new Color(1f, 1f, 1f, 0f);
        for (var x = 0; x < texture.width; x++)
        for (var y = 0; y < texture.height; y++)
            texture.SetPixel(x, y, clear);
        for (var r = 0; r < 4; r++)
        for (var c = r; c <= 6 - r; c++)
        {
            switch (direction)
            {
                case Direction.Down: texture.SetPixel(c, 3 - r, Color.white); break;
                case Direction.Left: texture.SetPixel(3 - r, c, Color.white); break;
                default: texture.SetPixel(r, c, Color.white); break;
            }
        }

        texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = texture.name;
        Sprites[direction] = sprite;
        return sprite;
    }

    internal static Image Add(Transform parent, Direction direction, Color color)
    {
        var sprite = Get(direction);
        var image = new GameObject("digital_storage_arrow", typeof(RectTransform)).AddComponent<Image>();
        image.transform.SetParent(parent, false);
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        image.rectTransform.sizeDelta = new Vector2(sprite.rect.width * UnitsPerPixel, sprite.rect.height * UnitsPerPixel);
        return image;
    }
}
