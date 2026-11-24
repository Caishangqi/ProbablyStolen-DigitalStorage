using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Platform.Content;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
public sealed class IoPortNativeStateTests
{
    [Test]
    public void TryDecode_MissingData_UsesDefaultSettingsAndNoJobs()
    {
        Assert.That(IoPortNativeState.TryDecode(null, null, out var absent), Is.True);
        Assert.That(IoPortNativeState.TryDecode(2, null, out var schemaOnly), Is.True);
        Assert.That(absent!.Settings.SameAs(IoPortSettings.Default), Is.True);
        Assert.That(absent.Settings.Running, Is.False, "A new machine waits for Start.");
        Assert.That(absent.Encode(), Is.EqualTo("0,2,0||||||"));
        Assert.That(schemaOnly!.Encode(), Is.EqualTo(absent.Encode()));
    }

    [Test]
    public void TryDecode_RoundTripsSettingsAndJobs()
    {
        var state = IoPortNativeState.Default
            .WithSettings(new IoPortSettings(IoPortDirection.Reverse, IoPortCompletionMode.Full, running: false))
            .WithJob(0, 12, 100).WithJob(5, 7, 1);

        Assert.That(IoPortNativeState.TryDecode(2, state.Encode(), out var decoded), Is.True);
        Assert.That(decoded!.Encode(), Is.EqualTo("1,1,0|12,100|||||7,1"));
        Assert.That(decoded.CommittedRoots(0, 12), Is.EqualTo(100));
        Assert.That(decoded.CommittedRoots(0, 13), Is.Zero, "Another cell in the slot starts a new job.");
        Assert.That(decoded.CommittedRoots(5, 7), Is.EqualTo(1));
    }

    [Test]
    public void TryDecode_FirstSchemaNetworkModeKeepsDirectionCompletionAndCounts()
    {
        Assert.That(IoPortNativeState.TryDecode(1, "1,1,0|12,0,100|||||", out var state), Is.True);

        Assert.That(state!.Settings.Direction, Is.EqualTo(IoPortDirection.Reverse));
        Assert.That(state.Settings.CompletionMode, Is.EqualTo(IoPortCompletionMode.Empty));
        Assert.That(state.Settings.Running, Is.False, "A converted machine waits for Start like a new one.");
        Assert.That(state.CommittedRoots(0, 12), Is.EqualTo(100));
    }

    [TestCase(1, "0,0,2|12,13,4|||||")]
    [TestCase(1, "1,0,2|12,13,4|||||")]
    [TestCase(3, "0,2,1||||||")]
    [TestCase(2, "0,2,1|12,100\0|||||")]
    [TestCase(2, "0,2,2||||||")]
    [TestCase(2, "0,2,1|0,5|||||")]
    public void TryDecode_RemovedModeUnknownSchemaOrMalformedDataReturnsNoState(int schema, string data)
    {
        Assert.That(IoPortNativeState.TryDecode(schema, data, out var state), Is.False);
        Assert.That(state, Is.Null, "No default replaces saved data that cannot be read.");
    }

    [Test]
    public void WithSettings_DirectionChangeStartsNewJobsAndOtherChangesKeepThem()
    {
        var state = IoPortNativeState.Default.WithJob(1, 20, 29);

        var completion = state.WithSettings(state.Settings.WithCompletionMode(IoPortCompletionMode.Empty).WithRunning(false));
        var reversed = state.WithSettings(state.Settings.ReverseDirection());

        Assert.That(completion.CommittedRoots(1, 20), Is.EqualTo(29));
        Assert.That(reversed.CommittedRoots(1, 20), Is.Zero);
    }

    [Test]
    public void WithoutJobs_ClearsEveryCounterAndKeepsSettings()
    {
        var state = IoPortNativeState.Default.WithJob(0, 10, 5).WithJob(1, 11, 6);

        var cleared = state.WithoutJobs();

        Assert.That(cleared.Encode(), Is.EqualTo(IoPortNativeState.Default.Encode()));
        Assert.That(state.WithJob(0, 10, 0).CommittedRoots(0, 10), Is.Zero, "A zero count keeps no record.");
    }
}
