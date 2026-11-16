using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
internal sealed class StorageDiagnosticsTests
{
    [Test]
    public void Failure_PreservesExceptionChainAndDeduplicatesPerInstanceAndEvent()
    {
        var messages = new List<string>();
        var diagnostics = new StorageDiagnostics(messages.Add, () => false);
        var instance = new object();
        var error = CaptureException();

        diagnostics.Failure(instance, "cell_read_failed", "read failed", error, () => "instance=7 schema=2");
        diagnostics.Failure(instance, "cell_read_failed", "read failed", error);

        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0], Does.Contain("event=cell_read_failed"));
        Assert.That(messages[0], Does.Contain("outer"));
        Assert.That(messages[0], Does.Contain("inner"));
        Assert.That(messages[0], Does.Contain("StorageDiagnosticsTests"));
        Assert.That(messages[0], Does.Contain("instance=7 schema=2"));
    }

    [Test]
    public void Verbose_UsesCurrentToggleAndBoundedBudget()
    {
        var messages = new List<string>();
        var enabled = false;
        var diagnostics = new StorageDiagnostics(messages.Add, () => enabled);

        diagnostics.Verbose("storage_state", "state");
        enabled = true;
        for (var index = 0; index < 100; index++) diagnostics.Verbose("storage_state", index.ToString());

        Assert.That(messages, Has.Count.EqualTo(64));
        Assert.That(messages[0], Does.Contain("0 event=storage_state"));
    }

    [Test]
    public void Failure_ContinuesWhenContextOrSinkThrows()
    {
        var messages = new List<string>();
        var contextCalls = 0;
        var diagnostics = new StorageDiagnostics(_ => throw new InvalidOperationException("sink"), () => throw new InvalidOperationException("toggle"));

        Action failure = () => diagnostics.Failure(new object(), "module_bay_failed", "failure",
            new Exception("original"), () =>
            {
                contextCalls++;
                throw new Exception("context");
            });
        Action verbose = () => diagnostics.Verbose("state", "state");
        Assert.DoesNotThrow(failure);
        Assert.DoesNotThrow(verbose);
        Assert.That(contextCalls, Is.EqualTo(1));
        Assert.That(messages, Is.Empty);
    }

    [Test]
    public void Failure_DoesNotRetainInstance()
    {
        var diagnostics = new StorageDiagnostics(_ => { }, () => false);
        var weak = TrackTemporary(diagnostics);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(weak.IsAlive, Is.False);
    }

    [Test]
    public void Failure_MessageDoesNotSuppressFollowingExceptions()
    {
        var messages = new List<string>();
        var diagnostics = new StorageDiagnostics(messages.Add, () => false);
        var instance = new object();

        diagnostics.Failure(instance, "provider_failed", "no inventory");
        diagnostics.Failure(instance, "provider_failed", "failed", new Exception("first fault"));
        diagnostics.Failure(instance, "provider_failed", "failed", new Exception("second fault"));
        diagnostics.Failure(instance, "provider_failed", "failed", new Exception("first fault"));

        Assert.That(messages, Has.Count.EqualTo(3));
        Assert.That(messages[1], Does.Contain("first fault"));
        Assert.That(messages[2], Does.Contain("second fault"));
    }

    [Test]
    public void Failure_FailedOutputCanBeRetried()
    {
        var messages = new List<string>();
        var attempts = 0;
        var diagnostics = new StorageDiagnostics(message =>
        {
            if (++attempts == 1) throw new InvalidOperationException("sink");
            messages.Add(message);
        }, () => false);
        var instance = new object();
        var error = CaptureException();

        diagnostics.Failure(instance, "cell_read_failed", "failed", error);
        diagnostics.Failure(instance, "cell_read_failed", "failed", error);
        diagnostics.Failure(instance, "cell_read_failed", "failed", error);

        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0], Does.Contain("inner"));
    }

    [Test]
    public void Failure_TracksEachInstanceAndBoundsDistinctFaults()
    {
        var messages = new List<string>();
        var diagnostics = new StorageDiagnostics(messages.Add, () => false);
        var first = new object();
        var second = new object();
        for (var index = 0; index < 20; index++)
            diagnostics.Failure(first, "provider_failed", "failed", new Exception("fault " + index));
        diagnostics.Failure(second, "provider_failed", "failed", new Exception("fault 0"));

        Assert.That(messages, Has.Count.EqualTo(6));
        Assert.That(messages[3], Does.Contain("fault 3"));
        Assert.That(messages[4], Does.Contain("further distinct failures"));
        Assert.That(messages[5], Does.Contain("fault 0"));
    }

    [Test]
    public void Failure_IsIndependentOfTheVerboseBudget()
    {
        var messages = new List<string>();
        var diagnostics = new StorageDiagnostics(messages.Add, () => true);
        for (var index = 0; index < 100; index++) diagnostics.Verbose("storage_state", "state");
        diagnostics.Failure(new object(), "cell_read_failed", "failed", CaptureException());

        Assert.That(messages, Has.Count.EqualTo(65));
        Assert.That(messages[64], Does.Contain("outer").And.Contain("inner"));
    }

    [Test]
    public void Report_ThrowingExceptionFormatterRetainsOriginalMessageAndInnerException()
    {
        var messages = new List<string>();
        var error = new ThrowingFormatterException(CaptureException());

        Assert.DoesNotThrow((Action)(() => StorageDiagnostics.Report(messages.Add, "cell_compaction_failed", "failed", error)));
        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0], Does.Contain("original message").And.Contain("outer").And.Contain("inner"));
        Assert.DoesNotThrow((Action)(() => StorageDiagnostics.Report(_ => throw new Exception("sink"), "failed", "failed", error)));
    }

    [Test]
    public void ContextFields_FailedGetterDoesNotHideOtherFields()
    {
        var text = StorageDiagnostics.Field("uid", () => 42) + " " +
            StorageDiagnostics.Field("parent", () => throw new Exception("unavailable")) + " " +
            StorageDiagnostics.Field("host_uid", () => 7);

        Assert.That(text, Is.EqualTo("uid=42 parent=unavailable host_uid=7"));
        Assert.That(StorageDiagnostics.Field("item", () => new string('x', 300)), Has.Length.LessThan(150));
        Assert.That(StorageDiagnostics.Field("item", () => "a\r\nb"), Does.Not.Contain("\n").And.Not.Contain("\r"));
    }

    private sealed class ThrowingFormatterException : Exception
    {
        internal ThrowingFormatterException(Exception inner) : base("original message", inner) { }
        public override string ToString() => throw new InvalidOperationException("formatter");
    }

    private static Exception CaptureException()
    {
        try { throw new InvalidOperationException("outer", new ArgumentException("inner")); }
        catch (Exception error) { return error; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TrackTemporary(StorageDiagnostics diagnostics)
    {
        var instance = new object();
        diagnostics.Failure(instance, "cell_read_failed", "failure", new Exception("error"));
        return new WeakReference(instance);
    }
}
