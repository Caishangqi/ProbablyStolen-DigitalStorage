using System;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>What an item slot shows.</summary>
public enum PictureSource
{
    /// <summary>The layers captured from a live sample.</summary>
    Layers = 0,

    /// <summary>The sprite recorded in the item identity.</summary>
    Sprite = 1,

    /// <summary>A neutral placeholder, for a reference that no longer resolves.</summary>
    Placeholder = 2,

    /// <summary>Nothing.</summary>
    None = 3
}

/// <summary>Finds the live sample an item picture is captured from, trying each source in order.</summary>
public sealed class PictureSourceChain<TSample> where TSample : class
{
    private readonly Func<ItemKey, TSample?>[] _sources;

    /// <summary>Creates a chain; earlier sources win.</summary>
    public PictureSourceChain(params Func<ItemKey, TSample?>[] sources)
    {
        if (sources == null || sources.Length == 0) throw new ArgumentException("A picture needs at least one sample source.", nameof(sources));
        foreach (var source in sources)
        {
            if (source == null) throw new ArgumentException("A sample source cannot be null.", nameof(sources));
        }

        _sources = (Func<ItemKey, TSample?>[])sources.Clone();
    }

    /// <summary>Gets the first sample a source returns, or null. A throwing source is not skipped.</summary>
    public TSample? Find(ItemKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        foreach (var source in _sources)
        {
            var sample = source(key);
            if (sample != null) return sample;
        }

        return null;
    }

    /// <summary>Chooses what a slot shows: captured layers, then the identity sprite, then the placeholder if allowed.</summary>
    public static PictureSource Choose(bool hasLayers, bool hasSprite, bool allowPlaceholder) =>
        hasLayers ? PictureSource.Layers
        : hasSprite ? PictureSource.Sprite
        : allowPlaceholder ? PictureSource.Placeholder
        : PictureSource.None;
}
