namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Base prices (<c>unitValue</c>) for the mod's items. Native save loading restores the saved value, while
/// <see cref="ItemContentDefinitions"/> reapplies the current value to the six exact item identifiers on load.
/// </summary>
internal static class ItemPrices
{
    /// <summary>The network controller: the core of a network, powered by a battery.</summary>
    internal const long Controller = 600;

    /// <summary>The drive with fourteen cell bays.</summary>
    internal const long Drive = 450;

    /// <summary>The terminal.</summary>
    internal const long Terminal = 350;

    /// <summary>
    /// The cell housing, which is also the host of an assembled cell. The native value of a cell adds the component
    /// inside it, so assembly and disassembly keep the total value.
    /// </summary>
    internal const long CellHousing = 80;

    /// <summary>The 1k storage component, priced like the top tier of native modules.</summary>
    internal const long Component1K = 250;

    /// <summary>The native item type and tag that material buyers (engineers, Dr. Jackson) look for.</summary>
    /// <summary>The cell workbench's base value.</summary>
    internal const long Workbench = 400;

    /// <summary>The equal distribution module's base value.</summary>
    internal const long EqualDistributionModule = 500;

    /// <summary>The value of a type limit module whose roll cannot be read; a valid roll is priced from its values.</summary>
    internal const long TypeLimitModule = 300;

    /// <summary>The value each module bay expansion stage adds, as the native kit does.</summary>
    internal const long ExpansionStage = 100;

    internal const string MaterialType = "MATERIAL";
}
