using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProbablyStolen.DigitalStorage.Api.Integration;

/// <summary>
/// Marks an <see cref="IIntegrationPlugin"/> for discovery. The class needs a public parameterless constructor.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationPluginAttribute : Attribute
{
}

/// <summary>
/// The integration host only calls a plugin when the mod it names is enabled, and calls its phases in order:
/// <see cref="Register"/> once all mods are enabled, then
/// <see cref="OnRuntimeAvailable"/> for each loaded game and <see cref="OnRuntimeUnavailable"/> before the next one or
/// when Digital Storage stops. An exception in a phase disables only this plugin.
/// </summary>
public interface IIntegrationPlugin
{
    /// <summary>Gets the unique plugin id, for example <c>digital_storage:other_mod</c>.</summary>
    string PluginUid { get; }

    /// <summary>Gets the id of the mod this plugin works with.</summary>
    string RequiredModId { get; }

    /// <summary>Gets the versions of that mod the plugin was tested with; others start disabled.</summary>
    IReadOnlyCollection<string> VerifiedVersions { get; }

    /// <summary>
    /// Gets the Digital Storage localization key of the plugin's settings header, usually the other mod's name. Each
    /// plugin has its own header; it must differ from every other header.
    /// </summary>
    string SettingSectionKey { get; }

    /// <summary>Gets the Digital Storage localization key of the plugin's settings switch label.</summary>
    string SettingLabelKey { get; }

    /// <summary>Gets the Digital Storage localization key of the plugin's settings switch tooltip.</summary>
    string SettingTooltipKey { get; }

    /// <summary>
    /// Defines the plugin's own options below its switch. Called once, when the other mod is enabled and loaded,
    /// whether or not the switch is on, so players can set options before turning the plugin on.
    /// </summary>
    void RegisterSettings(IIntegrationConfig config);

    /// <summary>Requests patches; the host applies them only if all targets resolve.</summary>
    void Register(IIntegrationRegistration registration);

    /// <summary>Called when a game is loaded and the plugin is registered.</summary>
    void OnRuntimeAvailable(IDigitalStorageRuntime runtime);

    /// <summary>Called before the next game loads, when the switch turns off, or when Digital Storage stops.</summary>
    void OnRuntimeUnavailable();
}

/// <summary>Base class with empty phase callbacks. Plugins override the phases they need.</summary>
public abstract class IntegrationPlugin : IIntegrationPlugin
{
    /// <inheritdoc />
    public abstract string PluginUid { get; }

    /// <inheritdoc />
    public abstract string RequiredModId { get; }

    /// <inheritdoc />
    public abstract IReadOnlyCollection<string> VerifiedVersions { get; }

    /// <inheritdoc />
    public abstract string SettingSectionKey { get; }

    /// <inheritdoc />
    public abstract string SettingLabelKey { get; }

    /// <inheritdoc />
    public abstract string SettingTooltipKey { get; }

    /// <inheritdoc />
    public virtual void RegisterSettings(IIntegrationConfig config)
    {
    }

    /// <inheritdoc />
    public virtual void Register(IIntegrationRegistration registration)
    {
    }

    /// <inheritdoc />
    public virtual void OnRuntimeAvailable(IDigitalStorageRuntime runtime)
    {
    }

    /// <inheritdoc />
    public virtual void OnRuntimeUnavailable()
    {
    }
}

/// <summary>What a plugin can request in its register phase.</summary>
public interface IIntegrationRegistration
{
    /// <summary>Gets the version of the required mod from its manifest.</summary>
    string DetectedModVersion { get; }

    /// <summary>Gets the runtime; plugins may keep it for their patches.</summary>
    IDigitalStorageRuntime Runtime { get; }

    /// <summary>
    /// Requests a Harmony patch. <paramref name="target"/> is null when the plugin could not find the method; the host
    /// then marks the plugin incompatible and applies none of its patches. Patch methods must be static methods of the
    /// plugin's own types.
    /// </summary>
    void Patch(MethodBase? target, MethodInfo? prefix = null, MethodInfo? postfix = null,
        MethodInfo? transpiler = null, MethodInfo? finalizer = null);

    /// <summary>Marks the plugin incompatible with a reason, for checks other than a missing target.</summary>
    void Fail(string reason);

    /// <summary>
    /// Adds a check the host runs after it applied the plugin's patches, for example whether a transpiler found the
    /// call it rewrites. A check that returns a reason marks the plugin incompatible and removes its patches.
    /// </summary>
    void Verify(Func<string?> check);
}
