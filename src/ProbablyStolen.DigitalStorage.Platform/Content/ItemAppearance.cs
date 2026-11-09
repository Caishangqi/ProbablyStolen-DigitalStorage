using System;
using ProbablyStolen.DigitalStorage.Common.Initialization;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class ItemAppearance
{
    private readonly string _modId;

    internal ItemAppearance(ModuleIdentity identity)
    {
        _modId = (identity ?? throw new ArgumentNullException(nameof(identity))).Id;
    }

    internal void EnsureReady(string spriteKey)
    {
        if (UISettings.current == null || UISettings.current.gridSize != 16)
        {
            throw new InvalidOperationException("Digital Storage requires the verified 16-pixel item grid.");
        }

        var mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == _modId);
        if (mod == null || !mod.Sprites.TryGetValue(spriteKey, out var sprite) || sprite == null)
        {
            throw new InvalidOperationException($"Digital Storage sprite is not ready: {_modId}/{spriteKey}.");
        }

        if (sprite.rect.width <= 0 || sprite.rect.height <= 0 ||
            sprite.rect.width % 16 != 0 || sprite.rect.height % 16 != 0)
        {
            throw new InvalidOperationException($"Digital Storage sprite is not grid aligned: {spriteKey}.");
        }
    }

    internal void SetPreparedSprite(GameItem item, string spriteKey)
    {
        // SetSprite only marks a dirty flag that the live item view consumes on its next selection pass.
        item.useSpriteFromMod = true;
        item.SetSpriteQuick(_modId, spriteKey);
    }

    internal void Apply(GameItem item, string spriteKey, int width, int height)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        EnsureReady(spriteKey);
        var sprite = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == _modId).Sprites[spriteKey];
        if (width <= 0 || height <= 0 || sprite.rect.width != width * 16 || sprite.rect.height != height * 16)
        {
            throw new InvalidOperationException($"Digital Storage sprite dimensions do not match its footprint: {spriteKey}.");
        }

        item.SetSpriteAndShapeFromMod(_modId, spriteKey);
        item.SetShape(new GridShapeBuilder(width, height).SetDataFill(1));
        if (!item.useSpriteFromMod || item.spriteAtlasPath != _modId || item.spritePath != spriteKey ||
            item.shape.width != width || item.shape.height != height)
        {
            throw new InvalidOperationException($"Digital Storage appearance binding failed: {spriteKey}.");
        }
    }
}
