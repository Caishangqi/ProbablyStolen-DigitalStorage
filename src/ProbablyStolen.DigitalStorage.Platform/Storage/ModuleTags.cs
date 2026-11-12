using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// The rolled values of a type limit module item, written once when the item is created. Missing or unreadable
/// values give a null roll, which the module treats as invalid so it never raises the type limit.
/// </summary>
internal static class ModuleTags
{
    internal const string QualityTag = "digital_storage.module.quality";
    internal const string AddTag = "digital_storage.module.add";
    internal const string PercentTag = "digital_storage.module.percent";

    /// <summary>Gets the roll of a module item, or null when any value is missing or unknown.</summary>
    internal static TypeLimitRoll? Read(GameItem module)
    {
        if (!module.IsTag(QualityTag) || !module.IsTag(AddTag) || !module.IsTag(PercentTag)) return null;
        var quality = Quality(module.GetTagReadonly(QualityTag).GetString());
        if (quality == null) return null;
        return new TypeLimitRoll(quality.Value, module.GetTagReadonly(AddTag).GetInt(), module.GetTagReadonly(PercentTag).GetInt());
    }

    /// <summary>Writes a roll onto a new module item.</summary>
    internal static void Write(GameItem module, TypeLimitRoll roll)
    {
        module.ModifyTag(QualityTag, state => state.Enable().SetString(QualityText(roll.Quality)));
        module.ModifyTag(AddTag, state => state.Enable().SetInt(roll.Add));
        module.ModifyTag(PercentTag, state => state.Enable().SetInt(roll.Percent));
    }

    /// <summary>Gets the stable text of a quality, also used in sprite and localization keys.</summary>
    internal static string QualityText(ModuleQuality quality) => quality switch
    {
        ModuleQuality.Rare => "rare",
        ModuleQuality.Epic => "epic",
        _ => "common"
    };

    private static ModuleQuality? Quality(string? text) => text switch
    {
        "common" => ModuleQuality.Common,
        "rare" => ModuleQuality.Rare,
        "epic" => ModuleQuality.Epic,
        _ => null
    };
}
