using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolen.DigitalStorage.Platform.Compatibility;

/// <summary>Applies and removes single Harmony patches.</summary>
public interface IGamePatchApplier
{
    /// <summary>Applies the patch methods to the resolved target; throws when Harmony refuses them.</summary>
    void Apply(MethodBase target, GamePatch patch);

    /// <summary>Removes this patch's methods from the target, leaving other patches on it in place.</summary>
    void Remove(MethodBase target, GamePatch patch);
}

/// <summary>How a group ended up.</summary>
public enum GamePatchState
{
    /// <summary>Every patch of the group is applied.</summary>
    Applied,

    /// <summary>A member is missing from the running build; nothing of the group is applied.</summary>
    Unavailable,

    /// <summary>Harmony refused a patch; the group's applied patches were removed again.</summary>
    Failed
}

/// <summary>The outcome of one group.</summary>
public sealed class GamePatchResult
{
    internal GamePatchResult(GamePatchGroup group, GamePatchState state, IReadOnlyList<string> missing, Exception? error)
    {
        Feature = group.Feature;
        Tier = group.Tier;
        State = state;
        Missing = missing;
        Error = error;
    }

    /// <summary>Gets the feature the group serves.</summary>
    public GameFeature Feature { get; }

    /// <summary>Gets the group tier.</summary>
    public GamePatchTier Tier { get; }

    /// <summary>Gets the outcome.</summary>
    public GamePatchState State { get; }

    /// <summary>Gets the missing members as <c>Type.Member</c>; empty unless unavailable.</summary>
    public IReadOnlyList<string> Missing { get; }

    /// <summary>Gets Harmony's refusal; null unless failed.</summary>
    public Exception? Error { get; }

    /// <summary>Describes the outcome in one line for the log.</summary>
    public string Describe() => State switch
    {
        GamePatchState.Applied => $"{Feature}: applied",
        GamePatchState.Unavailable => $"{Feature} ({Tier}): unavailable, missing {string.Join(", ", Missing)}",
        _ => $"{Feature} ({Tier}): failed ({Error?.GetType().Name}: {Error?.Message})"
    };
}

/// <summary>
/// The running game build lacks a member that keeps stored items intact. Digital Storage does not start, because
/// running without it could change or lose stored items.
/// </summary>
public sealed class GameIncompatibleException : Exception
{
    internal GameIncompatibleException(IReadOnlyList<GamePatchResult> blocking)
        : base("Digital Storage does not support this game build and did not start: " +
            string.Join("; ", blocking.Select(result => result.Describe())) +
            ". Stored items cannot be restored safely on this build; do not save games that contain Digital Storage items.",
            blocking.FirstOrDefault(result => result.Error != null)?.Error) =>
        Blocking = blocking;

    /// <summary>Gets the integrity groups that could not be applied.</summary>
    public IReadOnlyList<GamePatchResult> Blocking { get; }
}

/// <summary>
/// Applies patch groups for the running game build. Every target is looked up before anything is patched; a group
/// with a missing member is skipped whole, and a missing integrity member stops the mod before any patch is applied.
/// </summary>
public static class GamePatchInstaller
{
    /// <summary>Applies the groups and publishes which are active in <see cref="GamePatchStatus"/>.</summary>
    /// <exception cref="GameIncompatibleException">An integrity group cannot be applied.</exception>
    public static IReadOnlyList<GamePatchResult> Install(IReadOnlyList<GamePatchGroup> groups, IGamePatchApplier applier)
    {
        if (groups == null) throw new ArgumentNullException(nameof(groups));
        if (applier == null) throw new ArgumentNullException(nameof(applier));
        if (groups.Select(group => group.Feature).Distinct().Count() != groups.Count)
            throw new ArgumentException("Each feature has one patch group.", nameof(groups));

        GamePatchStatus.Publish(Array.Empty<GameFeature>());
        var resolved = groups.Select(group => (Group: group, Targets: Resolve(group, out var missing), Missing: missing)).ToArray();
        var blocking = resolved
            .Where(entry => entry.Group.Tier == GamePatchTier.Integrity && entry.Missing.Count > 0)
            .Select(entry => new GamePatchResult(entry.Group, GamePatchState.Unavailable, entry.Missing, null))
            .ToArray();
        if (blocking.Length > 0) throw new GameIncompatibleException(blocking);

        var results = new List<GamePatchResult>(groups.Count);
        foreach (var (group, targets, missing) in resolved)
        {
            if (missing.Count > 0)
            {
                results.Add(new GamePatchResult(group, GamePatchState.Unavailable, missing, null));
                continue;
            }

            var error = Apply(group, targets, applier);
            var result = new GamePatchResult(group, error == null ? GamePatchState.Applied : GamePatchState.Failed,
                Array.Empty<string>(), error);
            if (error != null && group.Tier == GamePatchTier.Integrity) throw new GameIncompatibleException(new[] { result });
            results.Add(result);
        }

        GamePatchStatus.Publish(results.Where(result => result.State == GamePatchState.Applied).Select(result => result.Feature));
        return results;
    }

    private static MethodBase[] Resolve(GamePatchGroup group, out IReadOnlyList<string> missing)
    {
        var absent = new List<string>();
        var targets = new MethodBase[group.Patches.Count];
        for (var i = 0; i < targets.Length; i++)
        {
            var patch = group.Patches[i];
            MethodBase? target = null;
            // A build may add overloads the lookup did not name, or drop a type the lookup names; both count as missing.
            try { target = patch.Resolve(); }
            catch (Exception) { target = null; }
            if (target == null) absent.Add(patch.Target);
            else targets[i] = target;
        }

        foreach (var (member, present) in group.Requirements)
        {
            bool found;
            try { found = present(); }
            catch (Exception) { found = false; }
            if (!found) absent.Add(member);
        }

        missing = absent;
        return targets;
    }

    private static Exception? Apply(GamePatchGroup group, MethodBase[] targets, IGamePatchApplier applier)
    {
        var applied = 0;
        try
        {
            for (; applied < targets.Length; applied++) applier.Apply(targets[applied], group.Patches[applied]);
            return null;
        }
        catch (Exception error)
        {
            // Harmony records a patch only once its replacement method is built, so the refused one left nothing behind.
            for (var i = applied - 1; i >= 0; i--)
            {
                try { applier.Remove(targets[i], group.Patches[i]); }
                catch (Exception) { /* The loader removes every patch under this mod's id when it stops the mod. */ }
            }

            return error;
        }
    }
}

/// <summary>Which features have their patch group applied in this session; checked before offering an entry point.</summary>
public static class GamePatchStatus
{
    private static HashSet<GameFeature> _active = new();

    /// <summary>Gets whether the feature's patch group is applied.</summary>
    public static bool IsActive(GameFeature feature) => _active.Contains(feature);

    /// <summary>Replaces the active set; an empty set when the mod stops.</summary>
    public static void Publish(IEnumerable<GameFeature> features) => _active = new HashSet<GameFeature>(features);
}
