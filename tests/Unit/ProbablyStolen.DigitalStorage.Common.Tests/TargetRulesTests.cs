using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class TargetRulesTests
{
    private const string Machine = "machine";
    private const string Cell = "cell";
    private const string ForeignMachine = "foreign_machine";

    private static readonly Func<string, string, bool> Native = (_, target) => target is Machine or ForeignMachine;
    private static readonly Func<string, string, bool> CellRule = (_, target) => target == Cell;

    [TestCase(Machine, true)]
    [TestCase(Cell, true)]
    [TestCase("other", false)]
    public void OrWith_AllowsWhenNativeOrRuleAllows(string target, bool expected)
    {
        var composed = TargetRules.OrWith(Native, CellRule);

        Assert.That(composed("kit", target), Is.EqualTo(expected));
    }

    [Test]
    public void OrWith_OrsAMulticastChainInsteadOfTakingTheLastResult()
    {
        var chain = Native + ((_, _) => false);

        var composed = TargetRules.OrWith(chain, CellRule);

        Assert.That(chain("kit", Machine), Is.False, "direct invocation of a multicast chain returns only the last result");
        Assert.That(composed("kit", Machine), Is.True);
    }

    [Test]
    public void OrWith_StaysCorrectWhenALaterWrapperInvokesItDirectly()
    {
        var composed = TargetRules.OrWith(Native, CellRule);
        Func<string, string, bool> wrapper = (tool, target) => target != ForeignMachine && composed(tool, target);

        Assert.That(wrapper("kit", Machine), Is.True);
        Assert.That(wrapper("kit", Cell), Is.True);
        Assert.That(wrapper("kit", ForeignMachine), Is.False);
    }

    [Test]
    public void OrWith_KeepsAnEarlierWrapperRefusalForTargetsTheRuleDoesNotAdmit()
    {
        Func<string, string, bool> wrapper = (tool, target) => target != ForeignMachine && Native(tool, target);

        var composed = TargetRules.OrWith(wrapper, CellRule);

        Assert.That(composed("kit", Machine), Is.True);
        Assert.That(composed("kit", Cell), Is.True);
        Assert.That(composed("kit", ForeignMachine), Is.False);
    }

    [Test]
    public void OrWith_ReturnsTheRuleWhenThereIsNoPreviousChain()
    {
        var composed = TargetRules.OrWith(null, CellRule);

        Assert.That(composed, Is.SameAs(CellRule));
    }

    [Test]
    public void OrWith_RunsEveryDelegateOnceWithPreviousFirst()
    {
        var calls = "";
        Func<string, string, bool> first = (_, _) => { calls += "a"; return true; };
        Func<string, string, bool> second = (_, _) => { calls += "b"; return false; };
        Func<string, string, bool> rule = (_, _) => { calls += "r"; return false; };

        var allowed = TargetRules.OrWith(first + second, rule)("kit", Machine);

        Assert.That(allowed, Is.True);
        Assert.That(calls, Is.EqualTo("abr"));
    }

    [Test]
    public void OrWith_PropagatesAnExceptionAndStopsLikeTheNativeReduction()
    {
        var ruleRan = false;
        Func<string, string, bool> failing = (_, _) => throw new InvalidOperationException("native rule failed");
        Func<string, string, bool> rule = (_, _) => ruleRan = true;
        Action check = () => TargetRules.OrWith(failing, rule)("kit", Machine);

        Assert.That(check, Throws.InvalidOperationException);
        Assert.That(ruleRan, Is.False);
    }

    [Test]
    public void OrWith_StillOrsWhenAnotherModAppendsAfterIt()
    {
        var composed = TargetRules.OrWith(Native, CellRule);
        var chain = composed + ((_, _) => false);
        var allowed = false;

        foreach (var check in chain.GetInvocationList()) allowed |= ((Func<string, string, bool>)check)("kit", Machine);

        Assert.That(allowed, Is.True, "the native reduction ORs our single delegate with later appended ones");
    }

    [Test]
    public void OrWith_RejectsAMissingRule()
    {
        Action compose = () => TargetRules.OrWith(Native, null!);

        Assert.That(compose, Throws.ArgumentNullException);
    }
}
