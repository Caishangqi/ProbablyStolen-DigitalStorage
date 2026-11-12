using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Creates cell module items: the type limit module, whose quality and values are rolled once and kept in tags, and
/// the equal distribution module. Both are 2x2 modules of a type only cell module bays accept.
/// </summary>
internal sealed class CellModuleFactory
{
    internal const string TypeLimitSpritePrefix = "module_type_expansion_";
    internal const string EqualDistributionSprite = "module_equal_distribution";
    internal const int Width = 2;
    internal const int Height = 2;
    private const string EffectTag = "ADDITIONAL_EFFECT_STRING";
    private readonly ItemAppearance _appearance;
    private readonly ItemContentSynchronization _contentSynchronization;
    private readonly ModText _text;

    internal CellModuleFactory(ItemAppearance appearance, ItemContentSynchronization contentSynchronization, ModText text)
    {
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _contentSynchronization = contentSynchronization ?? throw new ArgumentNullException(nameof(contentSynchronization));
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// Creates a type limit module with a roll of a weighted random quality, so a module from any source, such as the
    /// item spawner, has values. A vendor rolls again for its stock (<see cref="Roll"/>). While a save decodes, the
    /// saved state replaces the factory's tags (SaveManager.DecodeNodesInternal), so no roll is spent there and the restored
    /// values are shown once loaded.
    /// </summary>
    internal GameItem CreateTypeLimit()
    {
        foreach (var quality in new[] { ModuleQuality.Common, ModuleQuality.Rare, ModuleQuality.Epic })
            _appearance.EnsureReady(Sprite(quality));
        var item = Create(CellUpgradeInventory.TypeLimitId, Sprite(ModuleQuality.Common));
        item.onLoaded += Present;
        if (!SaveManager.IsDecoding)
            Roll(item, TypeLimitRolls.Roll(TypeLimitRolls.RollQuality(NativeRandom.Instance), NativeRandom.Instance));
        return item;
    }

    /// <summary>Creates an equal distribution module.</summary>
    internal GameItem CreateEqualDistribution()
    {
        _appearance.EnsureReady(EqualDistributionSprite);
        return Create(CellUpgradeInventory.EqualDistributionId, EqualDistributionSprite);
    }

    /// <summary>Writes a roll onto a new type limit module and shows it.</summary>
    internal void Roll(GameItem module, TypeLimitRoll roll)
    {
        if (module.identifier != CellUpgradeInventory.TypeLimitId) throw new ArgumentException("Not a type limit module.", nameof(module));
        ModuleTags.Write(module, roll ?? throw new ArgumentNullException(nameof(roll)));
        Present(module);
    }

    private GameItem Create(string itemId, string spriteKey)
    {
        var item = ItemDirectory.CreateEmptyItem(itemId);
        try
        {
            _appearance.Apply(item, spriteKey, Width, Height);
            NativeItemContentInitialization.Initialize(item, _contentSynchronization);
            ModuleHelper.InitModuleItem(item, CellUpgradeInventory.ModuleType, isShipModule: false);
            // Rolled values make every type limit module distinct; stacking would merge different values.
            item.mayStackItemFunc = (_, _) => false;
            item.maxSlotAmountItemFunc = (_, _, _) => 1;
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }

    private void Present(GameItem module)
    {
        var roll = ModuleTags.Read(module);
        if (roll == null || !TypeLimitRolls.IsValid(roll))
        {
            module.ModifyTag(EffectTag, state => state.Enable().SetString(_text.Get("module_type_limit_invalid")));
            return;
        }

        _appearance.SetPreparedSprite(module, Sprite(roll.Quality));
        var quality = _text.Get("module_quality_" + ModuleTags.QualityText(roll.Quality));
        module.ModifyTag(EffectTag, state => state.Enable().SetString(
            _text.Get("module_type_limit_effect", quality, roll.Add, roll.Percent)));
        NativeItemContentInitialization.Initialize(module, _contentSynchronization);
    }

    private static string Sprite(ModuleQuality quality) => TypeLimitSpritePrefix + ModuleTags.QualityText(quality);
}
