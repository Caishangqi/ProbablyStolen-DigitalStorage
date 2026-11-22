using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Compat.ClientDemandHighlight;

/// <summary>Highlights stored items using the current customer's native demand rules.</summary>
[IntegrationPlugin]
public sealed class ClientDemandHighlightPlugin : IntegrationPlugin
{
    private const string ModId = "hollow.clientdemandhighlight";
    private IIntegrationOption<bool>? _normal;
    private IIntegrationOption<bool>? _stolen;
    private IIntegrationOption<bool>? _contraband;
    private ITerminalDecorationRegistration? _decorations;

    /// <inheritdoc />
    public override string PluginUid => "digital_storage:client_demand_highlight";
    /// <inheritdoc />
    public override string RequiredModId => ModId;
    /// <inheritdoc />
    public override IReadOnlyCollection<string> VerifiedVersions { get; } = Array.AsReadOnly(new[] { "1.3.9" });
    /// <inheritdoc />
    public override string SettingSectionKey => "integration_client_demand_section";
    /// <inheritdoc />
    public override string SettingLabelKey => "integration_client_demand_label";
    /// <inheritdoc />
    public override string SettingTooltipKey => "integration_client_demand_tooltip";

    /// <inheritdoc />
    public override void RegisterSettings(IIntegrationConfig config)
    {
        _normal = config.DefineBool("normal", true, "integration_client_demand_normal", "integration_client_demand_normal_desc");
        _stolen = config.DefineBool("stolen", true, "integration_client_demand_stolen", "integration_client_demand_stolen_desc");
        _contraband = config.DefineBool("contraband", true, "integration_client_demand_contraband", "integration_client_demand_contraband_desc");
    }

    /// <inheritdoc />
    public override void Register(IIntegrationRegistration registration)
    {
        if (registration is not ITerminalDecorationRegistration decorations)
            throw new InvalidOperationException("The terminal decoration API is unavailable.");
        ClientDemandPatches.Register(registration);
        decorations.AddSlotDecoration("demand", "client_demand_border", Evaluate);
        _decorations = decorations;
    }

    /// <inheritdoc />
    public override void OnRuntimeAvailable(IDigitalStorageRuntime runtime) =>
        ClientDemandPatches.Invalidate = _decorations?.InvalidateSlotDecorations;

    /// <inheritdoc />
    public override void OnRuntimeUnavailable() => ClientDemandPatches.Invalidate = null;

    private IReadOnlyList<int?> Evaluate(IReadOnlyList<GameItem> items)
    {
        var client = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
        var inspection = client?.clientIntent == StoreClient.ClientIntent.INSPECTION;
        var barter = client?.clientIntent == StoreClient.ClientIntent.BARTER;
        var buying = client != null && (client.IsClientBuying() || barter);
        var kind = inspection ? DemandKind.Inspection : client == null || !buying ? DemandKind.None : DemandKind.Buying;
        var result = new int?[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item == null || !GeneralHelper.IsItemOwned(item)) continue;
            var wants = false;
            if (kind == DemandKind.Buying)
            {
                if (barter)
                {
                    var conditions = client!.barterOffer?.conditions;
                    if (conditions != null)
                    {
                        foreach (var condition in conditions)
                            if (condition != null && condition.IsItemValid(item)) { wants = true; break; }
                    }
                }
                else wants = client!.IsClientBuyingThisItem(item);
            }
            if (kind == DemandKind.Inspection || wants)
                result[index] = DemandClassifier.Classify(kind, buying, wants,
                    ContrabandHelper.IsContraband(item), StolenHelper.IsStolenItem(item),
                    _normal?.Value ?? true, _stolen?.Value ?? true, _contraband?.Value ?? true);
        }
        return result;
    }
}