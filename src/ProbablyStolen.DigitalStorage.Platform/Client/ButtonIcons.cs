using System;
using System.Collections.Generic;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class ButtonIcons : IDisposable
{
    internal const string Placeholder = "button_placeholder";
    private readonly Action<string> _error;
    private readonly Dictionary<string, Sprite> _sprites = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private Dictionary<string, Texture2D>? _textures;

    internal ButtonIcons(Action<string> error) => _error = error;

    internal bool Ready => _sprites.ContainsKey(Placeholder);

    internal void Load(LoadedMod mod)
    {
        _textures = mod.Textures;
        if (!TryCreate(Placeholder, out _))
        {
            _error("Digital Storage: button placeholder is unavailable.");
            return;
        }
        foreach (var key in new[]
        {
            "button_sort_name", "button_sort_amount", "button_sort_ascending", "button_sort_descending",
            "button_size_small", "button_size_medium", "button_size_tall", "button_size_full",
            "button_modules", "button_whitelist", "button_blacklist", "button_clear", "button_delete"
        }) TryCreate(key, out _);
    }

    internal void ShowUnavailable(ModText text)
    {
        const string windowId = "digital_storage.button_icons_unavailable";
        CustomUIManager.Window(windowId, text.Get("terminal_page_unavailable"), CustomUIManager.LAYER_PAPER)
            .AddWarning(text.Get("terminal_page_unavailable"))
            .AddCloseButton(() => CustomUIManager.Instance.CloseWindow(windowId))
            .Show();
    }

    internal Sprite Get(string key)
    {
        if (!Ready) throw new InvalidOperationException("Button placeholder is unavailable.");
        return TryCreate(key, out var sprite) ? sprite! : _sprites[Placeholder];
    }

    private bool TryCreate(string key, out Sprite? sprite)
    {
        if (_sprites.TryGetValue(key, out sprite)) return true;
        Sprite? created = null;
        try
        {
            if (_textures == null || !_textures.TryGetValue(key, out var texture) || texture == null ||
                texture.width != ButtonIconImage.Size || texture.height != ButtonIconImage.Size)
                throw new InvalidOperationException("Missing or invalid texture.");
            var pixels = texture.GetPixels32();
            var alpha = new byte[pixels.Length];
            for (var index = 0; index < pixels.Length; index++) alpha[index] = pixels[index].a;
            if (!ButtonIconImage.HasVisiblePixels(texture.width, texture.height, alpha))
                throw new InvalidOperationException("Button icon has no visible pixels.");
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            created = Sprite.Create(texture, new Rect(0, 0, ButtonIconImage.Size, ButtonIconImage.Size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            if (created == null) throw new InvalidOperationException("Sprite creation failed.");
            _sprites.Add(key, created);
            sprite = created;
            return true;
        }
        catch (Exception error)
        {
            if (created != null) UnityEngine.Object.Destroy(created);
            if (_reported.Add(key)) _error($"Digital Storage: button icon {key} unavailable ({error.Message}).");
            sprite = null;
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var sprite in _sprites.Values) UnityEngine.Object.Destroy(sprite);
        _sprites.Clear();
        _textures = null;
    }
}
