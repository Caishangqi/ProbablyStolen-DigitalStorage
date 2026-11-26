using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Compat.DsStocking;

/// <summary>
/// Lets one-click stocking of DS Stocking Helper take goods from online digital networks in the store. DS Stocking
/// Helper keeps choosing the goods with its own rules and budget; network goods join only the candidates of a stocking
/// run, and every unit leaves or returns through the public storage API.
/// </summary>
[IntegrationPlugin]
public sealed class DsStockingPlugin : IntegrationPlugin
{
    private IIntegrationOption<bool>? _stacks;

    /// <inheritdoc />
    public override string PluginUid => "digital_storage:ds_stocking";

    /// <inheritdoc />
    public override string RequiredModId => DsStockingTargets.ModId;

    /// <inheritdoc />
    public override IReadOnlyCollection<string> VerifiedVersions => DsStockingTargets.VerifiedVersions;

    /// <inheritdoc />
    public override string SettingSectionKey => "integration_dsstocking_section";

    /// <inheritdoc />
    public override string SettingLabelKey => "integration_dsstocking_label";

    /// <inheritdoc />
    public override string SettingTooltipKey => "integration_dsstocking_tooltip";

    /// <inheritdoc />
    public override void RegisterSettings(IIntegrationConfig config)
    {
        _stacks = config.DefineBool("stacks", true, "integration_dsstocking_stacks", "integration_dsstocking_stacks_desc");
        DsStockingPatches.OfferStacks = () => _stacks?.Value ?? true;
    }

    /// <inheritdoc />
    public override void Register(IIntegrationRegistration registration)
    {
        var run = DsStockingTargets.Run();
        var putUp = DsStockingTargets.PutUp();
        var sendBack = DsStockingTargets.SendBack();
        if (DsStockingTargets.Collect() == null) registration.Fail("the candidate scan was not found");
        if (DsStockingPatches.Resolve() is { } reason) registration.Fail(reason);
        registration.Patch(run, prefix: DsStockingPatches.RunPrefixMethod, finalizer: DsStockingPatches.RunFinalizerMethod,
            transpiler: DsStockingPatches.AddNetworkGoodsToScanMethod);
        registration.Patch(putUp, transpiler: DsStockingPatches.RedirectTransfersMethod);
        registration.Patch(sendBack, transpiler: DsStockingPatches.RedirectTransfersMethod);
        registration.Patch(DsStockingTargets.PutBack(), prefix: DsStockingPatches.PutBackPrefixMethod);
        // Network goods without a rewritten transfer would move natively off cell pages.
        registration.Verify(() => run != null && DsStockingPatches.RewrittenIn(run) != 1
            ? "the stocking scan call was not found exactly once" : null);
        registration.Verify(() => putUp != null && DsStockingPatches.RewrittenIn(putUp) == 0
            ? "the stocking transfer call was not found" : null);
        registration.Verify(() => sendBack != null && DsStockingPatches.RewrittenIn(sendBack) == 0
            ? "the budget rollback transfer call was not found" : null);
        DsStockingBridge.Attach(registration.Runtime);
    }

    /// <inheritdoc />
    public override void OnRuntimeUnavailable() => DsStockingBridge.Attach(null);

    /// <inheritdoc />
    public override void OnRuntimeAvailable(IDigitalStorageRuntime runtime) => DsStockingBridge.Attach(runtime);
}
