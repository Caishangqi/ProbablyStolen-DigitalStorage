using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Platform.Content;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class IoPortNativeStateTests
{
    [Test]
    public void TryWrite_WritesOnlyOverTheExpectedState()
    {
        var machine = new GameItem(IoPortItem.ItemId);
        var first = IoPortNativeState.Default.WithJob(0, 10, 100);
        Assert.That(IoPortNativeState.TryWrite(machine, IoPortNativeState.Default, first), Is.True);

        Assert.That(IoPortNativeState.TryWrite(machine, IoPortNativeState.Default, first.WithJob(0, 10, 101)), Is.False,
            "A stale expected state must not overwrite a newer count.");
        Assert.That(IoPortNativeState.TryRead(machine, out var saved), Is.True);
        Assert.That(saved!.CommittedRoots(0, 10), Is.EqualTo(100));
        Assert.That(machine.state.dict[IoPortNativeState.SchemaTag].GetInt(), Is.EqualTo(2));
    }

    [Test]
    public void TryWrite_UpgradesFirstSchemaTagsOnTheNextChange()
    {
        var machine = new GameItem(IoPortItem.ItemId);
        machine.ModifyTag(IoPortNativeState.SchemaTag, tag => tag.Enable().SetInt(1), skipValidate: true);
        machine.ModifyTag(IoPortNativeState.DataTag, tag => tag.Enable().SetString("1,0,2|12,0,7|||||"), skipValidate: true);
        Assert.That(IoPortNativeState.TryRead(machine, out var legacy), Is.True);

        Assert.That(IoPortNativeState.TryWrite(machine, legacy!, legacy!.WithSettings(legacy.Settings.WithRunning(false))), Is.True);

        Assert.That(machine.state.dict[IoPortNativeState.SchemaTag].GetInt(), Is.EqualTo(2));
        Assert.That(machine.state.dict[IoPortNativeState.DataTag].GetString(), Is.EqualTo("0,2,0|12,7|||||"));
    }

    [Test]
    public void TryWrite_LeavesRemovedCellToCellTagsUntouched()
    {
        var machine = new GameItem(IoPortItem.ItemId);
        machine.ModifyTag(IoPortNativeState.SchemaTag, tag => tag.Enable().SetInt(1), skipValidate: true);
        machine.ModifyTag(IoPortNativeState.DataTag, tag => tag.Enable().SetString("0,0,2|12,13,7|||||"), skipValidate: true);

        Assert.That(IoPortNativeState.TryRead(machine, out _), Is.False);
        Assert.That(IoPortNativeState.TryWrite(machine, IoPortNativeState.Default, IoPortNativeState.Default), Is.False);
        Assert.That(machine.state.dict[IoPortNativeState.DataTag].GetString(), Is.EqualTo("0,0,2|12,13,7|||||"));
    }
}
