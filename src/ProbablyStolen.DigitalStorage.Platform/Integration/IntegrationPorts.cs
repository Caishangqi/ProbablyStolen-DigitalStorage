using System;
using System.Reflection;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Platform.Integration;

/// <summary>Where an integration plugin stands; one log line is written per plugin and state.</summary>
internal enum IntegrationState
{
    /// <summary>The required mod is not enabled; the plugin is never called and nothing is logged.</summary>
    Absent,

    /// <summary>The required mod is enabled, but the plugin's settings switch is off.</summary>
    Disabled,

    /// <summary>The required mod is enabled but not running, for example stopped by the ban list.</summary>
    NotRunning,

    /// <summary>The plugin registered and its patches are applied.</summary>
    Active,

    /// <summary>A patch target is missing, a patch failed or a check failed; no patch of the plugin is applied.</summary>
    Incompatible,

    /// <summary>A plugin callback threw; no patch of the plugin is applied.</summary>
    Faulted
}

/// <summary>The game loader as the integration host needs it.</summary>
internal interface IIntegrationEnvironment
{
    /// <summary>Gets whether the player enabled the mod (fixed for the session; enabling needs a restart).</summary>
    bool IsModEnabled(string modId);

    /// <summary>Gets whether the mod is running now.</summary>
    bool IsModRunning(string modId);

    /// <summary>Gets the mod version from its manifest, or null.</summary>
    string? ModVersion(string modId);
}

/// <summary>Applies and removes the patches plugins request.</summary>
internal interface IIntegrationPatcher
{
    /// <summary>Applies one request; throws when Harmony refuses it.</summary>
    void Apply(PatchRequest request);

    /// <summary>Removes the patch methods of one request from its target; never removes other mods' patches.</summary>
    void Remove(PatchRequest request);
}

/// <summary>The settings header, switch and options of each plugin.</summary>
internal interface IIntegrationSettings
{
    /// <summary>
    /// Registers the plugin's header and switch, below every section defined before, and returns the switch reader and
    /// the config the plugin defines its own options with.
    /// </summary>
    /// <param name="plugin">The plugin.</param>
    /// <param name="modVersion">The detected version of the required mod.</param>
    /// <param name="verified">Whether that version is verified; verified versions default on, others off.</param>
    /// <param name="status">A localization key describing the plugin's state for the tooltip, or null.</param>
    (Func<bool> Switch, IIntegrationConfig Config) Define(IIntegrationPlugin plugin, string modVersion, bool verified,
        Func<string?> status);
}

/// <summary>One requested Harmony patch.</summary>
internal sealed class PatchRequest
{
    internal PatchRequest(MethodBase target, MethodInfo? prefix, MethodInfo? postfix, MethodInfo? transpiler,
        MethodInfo? finalizer)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Prefix = prefix;
        Postfix = postfix;
        Transpiler = transpiler;
        Finalizer = finalizer;
    }

    internal MethodBase Target { get; }

    internal MethodInfo? Prefix { get; }

    internal MethodInfo? Postfix { get; }

    internal MethodInfo? Transpiler { get; }

    internal MethodInfo? Finalizer { get; }
}
