using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Compat.BrewingExpansion;

/// <summary>Protects stored Brewing Expansion lore while retaining the native tooltip pipeline.</summary>
[IntegrationPlugin]
public sealed class BrewingExpansionPlugin : IntegrationPlugin, IIntegrationVersionPolicy
{
    private const string ModId = "brewing_expansion";
    private static bool _runtimeAvailable;

    /// <inheritdoc />
    public override string PluginUid => "digital_storage:brewing_expansion";
    /// <inheritdoc />
    public override string RequiredModId => ModId;
    /// <inheritdoc />
    public override IReadOnlyCollection<string> VerifiedVersions => Array.Empty<string>();
    /// <inheritdoc />
    public override string SettingSectionKey => "integration_brewing_section";
    /// <inheritdoc />
    public override string SettingLabelKey => "integration_brewing_label";
    /// <inheritdoc />
    public override string SettingTooltipKey => "integration_brewing_tooltip";

    bool IIntegrationVersionPolicy.SupportsVersion(string version) => BrewingExpansionVersions.Supports(version);

    /// <inheritdoc />
    public override bool CanEnableByDefault() => true;

    /// <inheritdoc />
    public override void Register(IIntegrationRegistration registration)
    {
        var arguments = new[] { typeof(RichTextBuilder), typeof(GameItem) };
        var target = AccessTools.DeclaredMethod("BrewingExpansion.Formal.ItemTooltipLayout:Tooltip", arguments);
        if (!BrewingTooltipTarget.HasExpectedSignature(target))
        {
            registration.Fail("tooltip_target_signature");
            return;
        }

        bool matches;
        try
        {
            var description = AccessTools.PropertySetter(typeof(GameItem), nameof(GameItem.shortDescription));
            var flavor = AccessTools.PropertySetter(typeof(GameItem), nameof(GameItem.flavorText));
            matches = description != null && flavor != null &&
                BrewingTooltipTarget.HasLoreWrites(PatchProcessor.ReadMethodBody(target!), description, flavor);
        }
        catch (Exception)
        {
            registration.Fail("tooltip_body_unreadable");
            return;
        }

        if (!matches)
        {
            registration.Fail("tooltip_lore_setters_missing");
            return;
        }

        var prefix = AccessTools.Method(typeof(BrewingExpansionPlugin), nameof(BeforeTooltip));
        var finalizer = AccessTools.Method(typeof(BrewingExpansionPlugin), nameof(AfterTooltip));
        if (prefix == null || finalizer == null)
        {
            registration.Fail("tooltip_patch_methods_missing");
            return;
        }

        registration.Patch(target, prefix: prefix, finalizer: finalizer);
    }

    /// <inheritdoc />
    public override void OnRuntimeAvailable(IDigitalStorageRuntime runtime) => _runtimeAvailable = true;

    /// <inheritdoc />
    public override void OnRuntimeUnavailable() => _runtimeAvailable = false;

    private static void BeforeTooltip(GameItem __1,
        out (TerminalTooltipScope? Scope, string Description, string Flavor) __state)
    {
        var scope = _runtimeAvailable ? TerminalTooltipScope.For(__1) : null;
        __state = scope == null ? default : (scope, __1.shortDescription, __1.flavorText);
    }

    private static Exception? AfterTooltip(Exception? __exception,
        (TerminalTooltipScope? Scope, string Description, string Flavor) __state)
    {
        __state.Scope?.Record(__state.Description, __state.Flavor);
        return __exception;
    }
}
