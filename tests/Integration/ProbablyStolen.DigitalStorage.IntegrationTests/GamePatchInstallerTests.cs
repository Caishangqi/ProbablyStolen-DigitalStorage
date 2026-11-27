using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
internal sealed class GamePatchInstallerTests
{
    private static readonly MethodInfo First = typeof(GamePatchInstallerTests).GetMethod(nameof(FirstTarget), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Second = typeof(GamePatchInstallerTests).GetMethod(nameof(SecondTarget), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Third = typeof(GamePatchInstallerTests).GetMethod(nameof(ThirdTarget), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Patch = typeof(GamePatchInstallerTests).GetMethod(nameof(PatchMethod), BindingFlags.NonPublic | BindingFlags.Static)!;

    [TearDown]
    public void ClearStatus() => GamePatchStatus.Publish(Array.Empty<GameFeature>());

    [Test]
    public void Install_AppliesEveryAvailableGroupInOrderAndPublishesThem()
    {
        var applier = new RecordingApplier();
        var results = GamePatchInstaller.Install(new[]
        {
            Group(GameFeature.CellPageRestore, GamePatchTier.Integrity, First),
            Group(GameFeature.ForwardFill, GamePatchTier.Enhancement, Second, Third)
        }, applier);

        Assert.That(results.Select(result => result.State), Is.All.EqualTo(GamePatchState.Applied));
        Assert.That(applier.Applied, Is.EqualTo(new[] { First, Second, Third }));
        Assert.That(GamePatchStatus.IsActive(GameFeature.CellPageRestore) && GamePatchStatus.IsActive(GameFeature.ForwardFill), Is.True);
    }

    [Test]
    public void Install_SkipsAWholeGroupWhenOneOfItsTargetsIsMissing()
    {
        var applier = new RecordingApplier();
        var results = GamePatchInstaller.Install(new[]
        {
            Group(GameFeature.CellPageRestore, GamePatchTier.Core, First),
            new GamePatchGroup(GameFeature.ForwardFill, GamePatchTier.Enhancement, Patched("Game.Second", Second), Patched("Game.Gone", null))
        }, applier);

        Assert.That(results[1].State, Is.EqualTo(GamePatchState.Unavailable));
        Assert.That(results[1].Missing, Is.EqualTo(new[] { "Game.Gone" }));
        Assert.That(applier.Applied, Is.EqualTo(new[] { First }));
        Assert.That(GamePatchStatus.IsActive(GameFeature.ForwardFill), Is.False);
        Assert.That(GamePatchStatus.IsActive(GameFeature.CellPageRestore), Is.True);
    }

    [Test]
    public void Install_TreatsAFalseOrThrowingRequirementAndAFailedLookupAsMissing()
    {
        var applier = new RecordingApplier();
        var results = GamePatchInstaller.Install(new[]
        {
            Group(GameFeature.LocaleRefresh, GamePatchTier.Enhancement, First).Requires("Game.field", () => false),
            Group(GameFeature.WindowBack, GamePatchTier.Enhancement, Second).Requires("Game.other", () => throw new InvalidOperationException()),
            new GamePatchGroup(GameFeature.ModConfigWindow, GamePatchTier.Enhancement,
                new GamePatch("Game.Overloaded", () => throw new AmbiguousMatchException(), postfix: Patch)),
            new GamePatchGroup(GameFeature.TypeLimitTooltip, GamePatchTier.Enhancement,
                new GamePatch("Game.RemovedType", () => throw new TypeLoadException("removed"), postfix: Patch))
        }, applier);

        Assert.That(results.Select(result => result.State), Is.All.EqualTo(GamePatchState.Unavailable));
        Assert.That(results.SelectMany(result => result.Missing),
            Is.EqualTo(new[] { "Game.field", "Game.other", "Game.Overloaded", "Game.RemovedType" }));
        Assert.That(applier.Applied, Is.Empty);
    }

    [Test]
    public void Install_RefusesBeforePatchingAnythingWhenAnIntegrityMemberIsMissing()
    {
        GamePatchStatus.Publish(new[] { GameFeature.WindowBack });
        var applier = new RecordingApplier();
        var error = Assert.Throws<GameIncompatibleException>(new Action(() =>
        {
            GamePatchInstaller.Install(new[]
        {
            Group(GameFeature.ForwardFill, GamePatchTier.Enhancement, First),
            new GamePatchGroup(GameFeature.CellPageRestore, GamePatchTier.Integrity, Patched("Game.Restore", null))
        }, applier);
        }))!;

        Assert.That(applier.Applied, Is.Empty);
        Assert.That(error.Blocking.Single().Missing, Is.EqualTo(new[] { "Game.Restore" }));
        Assert.That(error.Message, Does.Contain("Game.Restore").And.Contain("do not save"));
        Assert.That(GamePatchStatus.IsActive(GameFeature.WindowBack) || GamePatchStatus.IsActive(GameFeature.ForwardFill), Is.False);
    }

    [Test]
    public void Install_RemovesTheAppliedPartOfAGroupHarmonyRefusesAndKeepsTheOthers()
    {
        var applier = new RecordingApplier { RefuseOn = Third };
        var results = GamePatchInstaller.Install(new[]
        {
            Group(GameFeature.TerminalOpen, GamePatchTier.Core, Second, Third),
            Group(GameFeature.ForwardFill, GamePatchTier.Enhancement, First)
        }, applier);

        Assert.That(results[0].State, Is.EqualTo(GamePatchState.Failed));
        Assert.That(results[0].Error, Is.TypeOf<InvalidOperationException>());
        Assert.That(applier.Removed, Is.EqualTo(new[] { Second }));
        Assert.That(results[1].State, Is.EqualTo(GamePatchState.Applied));
        Assert.That(GamePatchStatus.IsActive(GameFeature.TerminalOpen), Is.False);
        Assert.That(GamePatchStatus.IsActive(GameFeature.ForwardFill), Is.True);
    }

    [Test]
    public void Install_StopsWhenHarmonyRefusesAnIntegrityGroup()
    {
        var applier = new RecordingApplier { RefuseOn = First };
        var error = Assert.Throws<GameIncompatibleException>(new Action(() =>
            { GamePatchInstaller.Install(new[] { Group(GameFeature.CellPageRestore, GamePatchTier.Integrity, First) }, applier); }))!;

        Assert.That(error.Blocking.Single().State, Is.EqualTo(GamePatchState.Failed));
        Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(applier.Removed, Is.Empty);
    }

    [Test]
    public void Install_RejectsTwoGroupsForOneFeature() =>
        Assert.Throws<ArgumentException>(new Action(() =>
        {
            GamePatchInstaller.Install(new[]
        {
            Group(GameFeature.TerminalOpen, GamePatchTier.Core, First),
            Group(GameFeature.TerminalOpen, GamePatchTier.Core, Second)
        }, new RecordingApplier());
        }));

    [Test]
    public void Definitions_RequireAPatchMethodAndAPatch()
    {
        Assert.Throws<ArgumentException>(new Action(() => { _ = new GamePatch("Game.Method", () => First); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = new GamePatchGroup(GameFeature.TerminalOpen, GamePatchTier.Core); }));
    }

    [Test]
    public void Describe_NamesTheMissingMembers()
    {
        var results = GamePatchInstaller.Install(new[]
        {
            Group(GameFeature.BorrowedPanelBand, GamePatchTier.Enhancement, First).Requires("Handler.bandWindow", () => false)
        }, new RecordingApplier());

        Assert.That(results[0].Describe(), Is.EqualTo("BorrowedPanelBand (Enhancement): unavailable, missing Handler.bandWindow"));
    }

    private static GamePatchGroup Group(GameFeature feature, GamePatchTier tier, params MethodInfo[] targets) =>
        new(feature, tier, targets.Select(target => Patched("Game." + target.Name, target)).ToArray());

    private static GamePatch Patched(string name, MethodBase? target) => new(name, () => target, postfix: Patch);

    private static void FirstTarget() { }

    private static void SecondTarget() { }

    private static void ThirdTarget() { }

    private static void PatchMethod() { }

    private sealed class RecordingApplier : IGamePatchApplier
    {
        internal MethodBase? RefuseOn { get; init; }

        internal List<MethodBase> Applied { get; } = new();

        internal List<MethodBase> Removed { get; } = new();

        public void Apply(MethodBase target, GamePatch patch)
        {
            if (ReferenceEquals(target, RefuseOn)) throw new InvalidOperationException("refused");
            Applied.Add(target);
        }

        public void Remove(MethodBase target, GamePatch patch) => Removed.Add(target);
    }
}
