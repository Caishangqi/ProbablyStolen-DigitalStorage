using System;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Machines;

/// <summary>Combines item targeting rules.</summary>
public static class TargetRules
{
    /// <summary>
    /// Returns one rule that allows a target when any rule in <paramref name="previous"/> or <paramref name="rule"/>
    /// allows it. Every rule is checked, previous ones first.
    /// </summary>
    public static Func<TSource, TTarget, bool> OrWith<TSource, TTarget>(
        Func<TSource, TTarget, bool>? previous, Func<TSource, TTarget, bool> rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        if (previous == null) return rule;
        var chain = previous.GetInvocationList().Cast<Func<TSource, TTarget, bool>>().ToArray();
        return (source, target) =>
        {
            var allowed = false;
            foreach (var check in chain) allowed |= check(source, target);
            return rule(source, target) | allowed;
        };
    }

    /// <summary>
    /// Returns one rule that refuses every target <paramref name="excluded"/> matches and otherwise allows a target
    /// when any rule in <paramref name="previous"/> allows it; without previous rules nothing is allowed.
    /// </summary>
    public static Func<TSource, TTarget, bool> Unless<TSource, TTarget>(
        Func<TSource, TTarget, bool>? previous, Func<TTarget, bool> excluded)
    {
        if (excluded == null) throw new ArgumentNullException(nameof(excluded));
        if (previous == null) return (_, _) => false;
        var chain = previous.GetInvocationList().Cast<Func<TSource, TTarget, bool>>().ToArray();
        return (source, target) =>
        {
            if (excluded(target)) return false;
            var allowed = false;
            foreach (var check in chain) allowed |= check(source, target);
            return allowed;
        };
    }
}
