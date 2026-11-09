namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Base prices (<c>unitValue</c>) of the mod's items, set where each item is created. Vanilla reference points in
/// Steam build 26496118: most machines 100-400, modules 30-250, storage coordinator 400, DNA manipulator 1000,
/// storage bay expansion kit 1063. The native save keeps <c>unitValue</c>, so items saved before a price change keep
/// their old value; only newly created items use these.
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
    internal const string MaterialType = "MATERIAL";
}
