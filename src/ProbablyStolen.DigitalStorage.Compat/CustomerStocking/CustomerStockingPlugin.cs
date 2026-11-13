using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Compat.CustomerStocking;

/// <summary>
/// Lets one-click stocking of the Customer Stocking mod take goods from online digital networks in its scan range.
/// Customer Stocking keeps choosing the goods with its own filters and budget; the network only becomes visible to its
/// scan during a stocking run, and every unit leaves or returns through the public storage API.
/// </summary>
[IntegrationPlugin]
public sealed class CustomerStockingPlugin : IntegrationPlugin
{
    /// <inheritdoc />
    public override string PluginUid => "digital_storage:customerstocking";

    /// <inheritdoc />
    public override string RequiredModId => StockingTargets.ModId;

    /// <inheritdoc />
    public override IReadOnlyCollection<string> VerifiedVersions => StockingTargets.VerifiedVersions;

    /// <inheritdoc />
    public override string SettingSectionKey => "integration_customerstocking_section";

    /// <inheritdoc />
    public override string SettingLabelKey => "integration_customerstocking_label";

    /// <inheritdoc />
    public override string SettingTooltipKey => "integration_customerstocking_tooltip";

    /// <inheritdoc />
    public override void Register(IIntegrationRegistration registration)
    {
        var place = StockingTargets.ExecutePlanBody();
        var restore = StockingTargets.RestoreBackCountDelta();
        registration.Patch(StockingTargets.RunAsync(), prefix: StockingPatches.RunPrefixMethod);
        registration.Patch(StockingTargets.BuildPlan(), prefix: StockingPatches.ScanPrefixMethod,
            finalizer: StockingPatches.ScanFinalizerMethod);
        registration.Patch(place, transpiler: StockingPatches.RedirectTransfersMethod);
        registration.Patch(restore, transpiler: StockingPatches.RedirectTransfersMethod);
        // Without a rewritten transfer the scan would see network goods that Customer Stocking then moves natively.
        registration.Verify(() => place != null && StockingPatches.RewrittenIn(place) == 0
            ? "the stocking transfer call was not found" : null);
        registration.Verify(() => restore != null && StockingPatches.RewrittenIn(restore) == 0
            ? "the budget rollback transfer call was not found" : null);
        StockingBridge.Attach(registration.Runtime);
    }

    /// <inheritdoc />
    public override void OnRuntimeUnavailable() => StockingBridge.EndRun();

    /// <inheritdoc />
    public override void OnRuntimeAvailable(IDigitalStorageRuntime runtime) => StockingBridge.Attach(runtime);
}
