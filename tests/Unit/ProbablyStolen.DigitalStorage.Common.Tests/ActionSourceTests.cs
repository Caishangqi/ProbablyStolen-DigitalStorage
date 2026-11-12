using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class ActionSourceTests
{
    [Test]
    public void OfMod_NamesOnlyTheMod()
    {
        var source = ActionSource.OfMod("customer_mod");

        Assert.That(source, Is.TypeOf<ModSource>());
        Assert.That(source.ModId, Is.EqualTo("customer_mod"));
        Assert.That(source.PlayerName, Is.Null);
        Assert.That(source.MachineId, Is.Null);
    }

    [Test]
    public void OfPlayerAndOfMachine_KeepTheirIdentities()
    {
        var player = ActionSource.OfPlayer("Player", "terminal-1");
        var machine = ActionSource.OfMachine("drive-1");

        Assert.That((player.PlayerName, player.MachineId, player.ModId), Is.EqualTo(("Player", "terminal-1", (string?)null)));
        Assert.That((machine.PlayerName, machine.MachineId, machine.ModId), Is.EqualTo(((string?)null, "drive-1", (string?)null)));
    }

    [Test]
    public void Empty_NamesNobody()
    {
        var source = ActionSource.Empty;

        Assert.That((source.PlayerName, source.MachineId, source.ModId), Is.EqualTo(((string?)null, (string?)null, (string?)null)));
        Assert.That(ActionSource.Empty, Is.SameAs(source));
    }

    [TestCase(null)]
    [TestCase("")]
    public void OfMod_RejectsMissingId(string? modId)
    {
        Assert.Throws<ArgumentException>(new Action(() => ActionSource.OfMod(modId!)));
    }

    [Test]
    public void MigrationSource_HasNoMod()
    {
        Assert.That(MigrationSource.Instance.ModId, Is.Null);
    }
}
