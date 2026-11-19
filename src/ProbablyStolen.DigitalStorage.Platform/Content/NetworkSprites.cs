using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Supplies the icons of the Wild network digital storage upgrades.
/// </summary>
internal sealed class NetworkSprites : IDisposable
{
    internal const string SheetKey = "network_digital";
    private const int IconSize = 36;
    // Only the first two cells of the sheet have upgrades; the third is reserved for a future tier.
    private const int IconCount = 2;
    private readonly ModLog _log;
    private readonly List<string> _iconKeys = new();
    private readonly List<Sprite> _icons = new();
    private LoadedMod? _mod;

    internal NetworkSprites(ModLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Gets the sprite key of upgrade <paramref name="tier"/> (1-based) as <c>AddNetworkUpgrade</c> takes it.</summary>
    internal static string IconKey(string modId, int tier) => modId + ":" + IconSpriteName(tier);

    private static string IconSpriteName(int tier) => SheetKey + "_" + tier;

    /// <summary>
    /// Cuts the upgrade icons out of the shared sheet into this mod's sprites. The sheet texture stays shared, so
    /// the source image is not cropped.
    /// </summary>
    internal void Load(LoadedMod mod)
    {
        ReleaseIcons();
        _mod = mod ?? throw new ArgumentNullException(nameof(mod));
        if (!mod.Sprites.TryGetValue(SheetKey, out var sheet) || sheet == null ||
            sheet.texture.width != IconSize * 3 || sheet.texture.height != IconSize)
        {
            _log.Error($"Digital Storage: the network icon sheet {SheetKey} is missing or not 108x36.");
            return;
        }

        for (var tier = 1; tier <= IconCount; tier++)
        {
            var icon = Sprite.Create(sheet.texture, new Rect((tier - 1) * IconSize, 0, IconSize, IconSize),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            var key = IconSpriteName(tier);
            mod.Sprites[key] = icon;
            _iconKeys.Add(key);
            _icons.Add(icon);
        }
    }

    public void Dispose()
    {
        ReleaseIcons();
        _mod = null;
    }

    private void ReleaseIcons()
    {
        for (var index = 0; index < _icons.Count; index++)
        {
            if (_mod != null && _mod.Sprites.TryGetValue(_iconKeys[index], out var present) && present == _icons[index])
                _mod.Sprites.Remove(_iconKeys[index]);
            UnityEngine.Object.Destroy(_icons[index]);
        }

        _icons.Clear();
        _iconKeys.Clear();
    }
}
