using System;
using ProbablyStolen.DigitalStorage.Common.Initialization;

namespace ProbablyStolen.DigitalStorage.Client.Initialization;

/// <summary>Describes a startup lifecycle state for module diagnostics.</summary>
public enum ModuleStartupPhase
{
    /// <summary>The module has initialized.</summary>
    Initialized,

    /// <summary>The module is enabled.</summary>
    Enabled,

    /// <summary>The module is disabled.</summary>
    Disabled
}

/// <summary>Formats module lifecycle states without depending on a game UI or engine.</summary>
public sealed class StartupStatusPresenter
{
    private readonly ModuleIdentity _identity;

    /// <summary>Creates a presenter for one module identity.</summary>
    /// <param name="identity">The identity included in each status message.</param>
    /// <exception cref="ArgumentNullException">The identity is null.</exception>
    public StartupStatusPresenter(ModuleIdentity identity)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    /// <summary>Formats a lifecycle phase as a diagnostic message.</summary>
    /// <param name="phase">The module lifecycle phase.</param>
    /// <returns>A stable display name and phase message.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The phase is not supported.</exception>
    public string Present(ModuleStartupPhase phase)
    {
        var description = phase switch
        {
            ModuleStartupPhase.Initialized => "initialized",
            ModuleStartupPhase.Enabled => "enabled",
            ModuleStartupPhase.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unsupported startup phase.")
        };

        return $"{_identity.DisplayName}: {description}.";
    }
}
