using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>A captured item picture: the native layers of a live sample, and the identity sprite as fallback.</summary>
internal sealed class ItemPicture
{
    internal ItemPicture(NativeItemView.Picture? layers, Sprite? fallback)
    {
        Layers = layers;
        Fallback = fallback;
    }

    internal NativeItemView.Picture? Layers { get; }
    internal Sprite? Fallback { get; }
}

/// <summary>
/// Captures item pictures for slot grids. Capturing redraws a live sample, so callers capture only when their
/// content changes and render from what they cached.
/// </summary>
internal sealed class ItemPictureCapture
{
    private readonly PictureSourceChain<GameItem> _samples;
    private readonly Action<string> _writeDiagnostic;
    private readonly string _owner;
    private readonly Sprite? _placeholder;
    private readonly HashSet<ItemKey> _failuresLogged = new();

    /// <summary>Creates a capture over sample sources; a placeholder, when given, stands in for an unresolved picture.</summary>
    internal ItemPictureCapture(PictureSourceChain<GameItem> samples, Action<string> writeDiagnostic, string owner,
        Sprite? placeholder = null)
    {
        _samples = samples ?? throw new ArgumentNullException(nameof(samples));
        _writeDiagnostic = writeDiagnostic ?? throw new ArgumentNullException(nameof(writeDiagnostic));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _placeholder = placeholder;
    }

    /// <summary>Captures the picture of a key; spriteKey is <c>atlas|sprite</c>.</summary>
    internal ItemPicture Capture(ItemKey key, string spriteKey)
    {
        var sample = _samples.Find(key) as GameItemElement;
        NativeItemView.Picture? layers = null;
        if (sample != null && !sample.IsDestroyed())
        {
            NativeItemView.Redraw(sample);
            layers = NativeItemView.Capture(sample);
        }

        return new ItemPicture(layers, LoadSprite(spriteKey));
    }

    /// <summary>Builds the fallback picture after a failed capture, logging each key once per session.</summary>
    internal ItemPicture Failed(ItemKey key, string spriteKey, Exception error)
    {
        if (_failuresLogged.Add(key))
        {
            try { _writeDiagnostic($"Digital Storage: {_owner} picture unavailable ({error.GetType().Name}); using sprite fallback."); }
            catch { /* A diagnostic callback cannot block an inventory display update. */ }
        }

        Sprite? fallback = null;
        try { fallback = LoadSprite(spriteKey); }
        catch { /* A missing atlas leaves the slot without a picture, without hiding its entry. */ }
        return new ItemPicture(null, fallback);
    }

    /// <summary>Forgets which failures were logged, for a new session.</summary>
    internal void ResetDiagnostics() => _failuresLogged.Clear();

    /// <summary>Shows a picture in a slot, or clears the slot when picture is null and no placeholder applies.</summary>
    /// <param name="view">The slot's layered view.</param>
    /// <param name="layers">The image pool of the layered view.</param>
    /// <param name="icon">The fallback image for a single sprite.</param>
    /// <param name="picture">The cached picture, or null when none was captured.</param>
    /// <param name="occupied">Whether the slot holds an entry; an empty slot is always cleared.</param>
    internal void Show(RectTransform? view, List<Image>? layers, Image? icon, ItemPicture? picture, bool occupied)
    {
        if (!occupied)
        {
            if (layers != null) NativeItemView.Clear(layers);
            if (icon != null) icon.enabled = false;
            return;
        }

        var mirrored = view != null && layers != null && picture?.Layers != null &&
            NativeItemView.Sync(view, layers, picture.Layers);
        if (!mirrored && layers != null) NativeItemView.Clear(layers);
        if (icon == null) return;
        var source = PictureSourceChain<GameItem>.Choose(mirrored, picture?.Fallback != null, _placeholder != null);
        icon.sprite = source switch
        {
            PictureSource.Sprite => picture!.Fallback,
            PictureSource.Placeholder => _placeholder,
            _ => null
        };
        icon.enabled = icon.sprite != null;
    }

    private static Sprite? LoadSprite(string key)
    {
        if (key == null) return null;
        var separator = key.IndexOf('|');
        if (separator <= 0 || separator >= key.Length - 1)
        {
            return null;
        }

        return RenderHandler.TryLoadFromAtlas(key.Substring(0, separator), key.Substring(separator + 1));
    }
}
