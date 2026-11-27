using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Mod.Patches;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.GameBindingTests;

/// <summary>
/// Looks up every patch target and required member in the game build the tests were compiled against, and checks that
/// Harmony can bind each patch parameter to it, without patching or running game code. Run once per supported build
/// (<c>mise run check-game-bindings</c>).
/// </summary>
[TestFixture]
internal sealed class GameBindingTests
{
    // Harmony supplies these itself; every other parameter binds to a target parameter of the same name.
    private static readonly HashSet<string> Injected = new(StringComparer.Ordinal)
    {
        "__instance", "__result", "__state", "__exception", "__originalMethod", "__args", "__runOriginal"
    };

    [TearDown]
    public void ClearStatus() => GamePatchStatus.Publish(Array.Empty<GameFeature>());

    [Test]
    public void EveryPatchGroupResolvesOnThisBuild()
    {
        var applier = new LookupOnly();
        var results = GamePatchInstaller.Install(GamePatchGroups.Create(), applier);
        TestContext.Out.WriteLine($"Assembly-CSharp {typeof(GameItem).Module.ModuleVersionId}; band scope {BorrowedPanelInput.Scope}");
        foreach (var result in results) TestContext.Out.WriteLine(result.Describe());

        Assert.That(results.Where(result => result.State != GamePatchState.Applied).Select(result => result.Describe()),
            Is.Empty);
        Assert.That(applier.Unbound, Is.Empty);
    }

    [Test]
    public void TheBandFollowsOneNativeRule() => Assert.That(BorrowedPanelInput.Scope, Is.Not.EqualTo(BandScope.None));

    private sealed class LookupOnly : IGamePatchApplier
    {
        internal List<string> Unbound { get; } = new();

        public void Apply(MethodBase target, GamePatch patch)
        {
            foreach (var method in new[] { patch.Prefix, patch.Postfix, patch.Finalizer })
            {
                if (method == null) continue;
                foreach (var parameter in method.GetParameters())
                {
                    if (!Binds(target, parameter))
                        Unbound.Add($"{patch.Target}: {method.DeclaringType?.Name}.{method.Name}({parameter.Name})");
                }
            }
        }

        public void Remove(MethodBase target, GamePatch patch)
        {
        }

        private static bool Binds(MethodBase target, ParameterInfo parameter)
        {
            var name = parameter.Name ?? string.Empty;
            if (Injected.Contains(name)) return true;
            if (name.StartsWith("___", StringComparison.Ordinal))
                return target.DeclaringType?.GetField(name.Substring(3),
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null;
            var original = target.GetParameters().FirstOrDefault(candidate => candidate.Name == name);
            return original != null && Unwrap(parameter.ParameterType).IsAssignableFrom(Unwrap(original.ParameterType));
        }

        private static Type Unwrap(Type type) => type.IsByRef ? type.GetElementType()! : type;
    }
}
