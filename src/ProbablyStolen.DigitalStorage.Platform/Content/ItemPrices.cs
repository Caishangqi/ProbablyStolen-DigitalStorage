namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Base prices (<c>unitValue</c>) for the mod's items. Native save loading restores the saved value, while
/// <see cref="ItemContentDefinitions"/> supplies current values for content synchronization.
/// </summary>
internal static class ItemPrices
{
    /// <summary>The network controller: the core of a network, powered by a battery.</summary>
    internal const long Controller = 400;

    /// <summary>The drive with fourteen cell bays.</summary>
    internal const long Drive = 350;

    /// <summary>The terminal.</summary>
    internal const long Terminal = 250;

    /// <summary>
    /// The cell housing, also used as the host value of an assembled cell. Native valuation adds the installed
    /// component separately, so the host does not include the component's price a second time.
    /// </summary>
    internal const long CellHousing = 80;

    /// <summary>The 1k storage component.</summary>
    internal const long Component1K = 100;

    /// <summary>The cell workbench's base value.</summary>
    internal const long Workbench = 300;

    /// <summary>The equal distribution module's base value.</summary>
    internal const long EqualDistributionModule = 200;

    /// <summary>The definition value of a type limit module; synchronization requires a valid roll to reprice it.</summary>
    internal const long TypeLimitModule = 100;

    /// <summary>The value each module bay expansion stage adds, as the native kit does.</summary>
    internal const long ExpansionStage = 100;

    internal const string MaterialType = "MATERIAL";
}
