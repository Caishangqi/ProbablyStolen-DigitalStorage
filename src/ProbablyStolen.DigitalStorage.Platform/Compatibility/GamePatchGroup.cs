using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProbablyStolen.DigitalStorage.Platform.Compatibility;

/// <summary>What a game member missing from the running build costs.</summary>
public enum GamePatchTier
{
    /// <summary>Keeps stored items intact through loading and native item scans; the mod refuses to start without it.</summary>
    Integrity,

    /// <summary>A storage interaction; without it only that interaction is turned off.</summary>
    Core,

    /// <summary>A convenience; without it the native behavior stays.</summary>
    Enhancement
}

/// <summary>One Harmony patch on one game method.</summary>
public sealed class GamePatch
{
    /// <summary>Describes a patch; at least one patch method is required.</summary>
    /// <param name="target">The game member as <c>Type.Member</c>, for logs.</param>
    /// <param name="resolve">
    /// Looks up the game method. Name the game type with <c>typeof</c> and the member with a literal at the lookup, so
    /// the game's mod scanner can read the target.
    /// </param>
    /// <param name="prefix">The prefix, or null.</param>
    /// <param name="postfix">The postfix, or null.</param>
    /// <param name="transpiler">The transpiler, or null.</param>
    /// <param name="finalizer">The finalizer, or null.</param>
    public GamePatch(string target, Func<MethodBase?> resolve, MethodInfo? prefix = null, MethodInfo? postfix = null,
        MethodInfo? transpiler = null, MethodInfo? finalizer = null)
    {
        Target = string.IsNullOrEmpty(target) ? throw new ArgumentException("A target name is required.", nameof(target)) : target;
        Resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        if (prefix == null && postfix == null && transpiler == null && finalizer == null)
            throw new ArgumentException($"The patch on {target} has no patch method.");
        Prefix = prefix;
        Postfix = postfix;
        Transpiler = transpiler;
        Finalizer = finalizer;
    }

    /// <summary>Gets the game member as <c>Type.Member</c>.</summary>
    public string Target { get; }

    internal Func<MethodBase?> Resolve { get; }

    /// <summary>Gets the prefix, or null.</summary>
    public MethodInfo? Prefix { get; }

    /// <summary>Gets the postfix, or null.</summary>
    public MethodInfo? Postfix { get; }

    /// <summary>Gets the transpiler, or null.</summary>
    public MethodInfo? Transpiler { get; }

    /// <summary>Gets the finalizer, or null.</summary>
    public MethodInfo? Finalizer { get; }
}

/// <summary>
/// Patches that only work together, such as a prefix and the postfix that ends its scope. The group is applied whole
/// or not at all.
/// </summary>
public sealed class GamePatchGroup
{
    private readonly List<(string Member, Func<bool> Present)> _requirements = new();

    /// <summary>Creates a group.</summary>
    /// <param name="feature">The feature the group serves.</param>
    /// <param name="tier">What a missing member costs.</param>
    /// <param name="patches">The patches; at least one.</param>
    public GamePatchGroup(GameFeature feature, GamePatchTier tier, params GamePatch[] patches)
    {
        Feature = feature;
        Tier = tier;
        if (patches == null || patches.Length == 0) throw new ArgumentException($"The group {feature} has no patch.", nameof(patches));
        Patches = patches;
    }

    /// <summary>Gets the feature the group serves.</summary>
    public GameFeature Feature { get; }

    /// <summary>Gets what a missing member costs.</summary>
    public GamePatchTier Tier { get; }

    internal IReadOnlyList<GamePatch> Patches { get; }

    internal IReadOnlyList<(string Member, Func<bool> Present)> Requirements => _requirements;

    /// <summary>Adds a game member the patches read or call besides their targets, such as a private field.</summary>
    /// <param name="member">The member as <c>Type.Member</c>, for logs.</param>
    /// <param name="present">Whether the running build has it in the expected shape.</param>
    /// <returns>This group.</returns>
    public GamePatchGroup Requires(string member, Func<bool> present)
    {
        if (string.IsNullOrEmpty(member)) throw new ArgumentException("A member name is required.", nameof(member));
        _requirements.Add((member, present ?? throw new ArgumentNullException(nameof(present))));
        return this;
    }
}
