using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Integration;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Compat.ProficiencySystem;

/// <summary>Keeps Proficiency System's price lines out of stored item types.</summary>
[IntegrationPlugin]
public sealed class ProficiencySystemPlugin : IntegrationPlugin
{
    private const string ModId = "prof.system";

    // Proficiency System rewrites these features whenever an item's price is read, so the same item would otherwise
    // become another type each time a tooltip, a sort or another mod asks for its value. The two markup ids come from
    // its earlier versions; it still removes them from items, so they can remain on items from older saves.
    private static readonly (string Entry, string Prefix)[] PriceLines =
    {
        ("sell_price_line", "prof.sell."),
        ("buy_price_line", "prof.buy."),
        ("legacy_markup", "PROF_MARKUP"),
        ("legacy_markup_v2", "prof_markup_v2")
    };

    /// <inheritdoc />
    public override string PluginUid => "digital_storage:prof_system";
    /// <inheritdoc />
    public override string RequiredModId => ModId;
    /// <inheritdoc />
    public override IReadOnlyCollection<string> VerifiedVersions => Array.Empty<string>();
    /// <inheritdoc />
    public override string SettingSectionKey => "integration_proficiency_section";
    /// <inheritdoc />
    public override string SettingLabelKey => "integration_proficiency_label";
    /// <inheritdoc />
    public override string SettingTooltipKey => "integration_proficiency_tooltip";

    /// <inheritdoc />
    public override bool CanEnableByDefault() => true;

    /// <inheritdoc />
    public override void RegisterContent(IContentRegistrationBatch batch)
    {
        var features = DeferredRegister<VolatileItemFeature>.Create(batch.Registries,
            DigitalStorageRegistries.VolatileItemFeature, ModId);
        foreach (var (entry, prefix) in PriceLines)
            features.Register(entry, () => new VolatileItemFeature(prefix));
    }
}
