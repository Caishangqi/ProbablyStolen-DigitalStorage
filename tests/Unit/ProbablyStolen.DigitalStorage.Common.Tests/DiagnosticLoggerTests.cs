using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
internal sealed class DiagnosticLoggerTests
{
    [Test]
    public void Register_KeepsDeclarationOrderAndRejectsInvalidDuplicatesAndLateWrites()
    {
        var categories = new LogCategories();
        var storage = categories.Register("log_storage", "log_category_storage", "Storage log", LogVerbosity.Info, true);
        var temp = categories.Register("log_temp", null, "Temporary log", LogVerbosity.Verbose, false);
        Assert.Throws<InvalidOperationException>(new Action(() => categories.Register("log_storage", null, "Storage", LogVerbosity.Info, true)));
        foreach (var id in new[] { "log_", "LOG_storage", "log_bad__id", "log_bad-id", "storage" })
            Assert.Throws<ArgumentException>(new Action(() => categories.Register(id, null, "Bad", LogVerbosity.Info, true)));
        categories.Freeze();
        Assert.That(categories.Entries, Is.EqualTo(new[] { storage, temp }));
        Assert.That(storage.DefaultEnabled, Is.True);
        Assert.That(temp.DefaultVerbosity, Is.EqualTo(LogVerbosity.Verbose));
        Assert.That(temp.DefaultEnabled, Is.False);
        Assert.Throws<InvalidOperationException>(new Action(() => categories.Register("log_late", null, "Late", LogVerbosity.Info, true)));
    }

    [Test]
    public void Write_ThresholdAndDisabledStateFilterBeforeEvaluatingFields()
    {
        var categories = new LogCategories();
        var category = categories.Register("log_storage", null, "Storage", LogVerbosity.Info, true);
        categories.Freeze();
        var enabled = true;
        var threshold = LogVerbosity.Warning;
        var output = new List<LogVerbosity>();
        var log = new DiagnosticLogger(categories, _ => new LogCategorySettings(enabled, threshold), (level, _) => output.Add(level));
        log.Write(category, LogVerbosity.Error, "error", "Error.");
        log.Write(category, LogVerbosity.Warning, "warning", "Warning.");
        var evaluated = false;
        log.Write(category, LogVerbosity.Info, "info", "Info.", () => { evaluated = true; return Array.Empty<LogField>(); });
        enabled = false;
        Assert.That(log.IsEnabled(category, LogVerbosity.Error), Is.False);
        enabled = true;
        Assert.That(log.IsEnabled(category, LogVerbosity.Warning), Is.True);
        Assert.That(log.IsEnabled(category, LogVerbosity.Info), Is.False);
        threshold = (LogVerbosity)73;
        Assert.That(log.IsEnabled(category, LogVerbosity.Info), Is.True);
        Assert.That(output, Is.EqualTo(new[] { LogVerbosity.Error, LogVerbosity.Warning }));
        Assert.That(evaluated, Is.False);
    }

    [Test]
    public void WriteText_UsesTheSameFilterAndKeepsLongTextWhole()
    {
        var categories = new LogCategories();
        var category = categories.Register("log_ui", null, "Interface log", LogVerbosity.Warning, true);
        categories.Freeze();
        var output = new List<string>();
        var log = new DiagnosticLogger(categories, _ => new LogCategorySettings(true, LogVerbosity.Warning),
            (_, line) => output.Add(line));
        var text = new string('x', 300) + "\nsecond line";

        Assert.That(log.WriteText(category, LogVerbosity.Verbose, "trace", "hidden"), Is.False);
        Assert.That(log.WriteText(category, LogVerbosity.Warning, "Bad Event", "rejected"), Is.False);
        Assert.That(log.WriteText(category, LogVerbosity.Warning, "trace", text), Is.True);

        Assert.That(output, Is.EqualTo(new[] { "category=LogUi level=warning event=trace " + text }));
    }

    [Test]
    public void Write_TempStopsAfter64RecordsAndOneNoticeWithoutUsingOtherBudgets()
    {
        var categories = new LogCategories();
        var temp = categories.Register("log_temp", null, "Temporary", LogVerbosity.Verbose, false);
        var other = categories.Register("log_storage", null, "Storage", LogVerbosity.Verbose, true);
        categories.Freeze();
        var output = new List<string>();
        var log = new DiagnosticLogger(categories, _ => new LogCategorySettings(true, LogVerbosity.Verbose), (_, line) => output.Add(line));
        for (var i = 0; i < 80; ++i) log.Write(temp, LogVerbosity.Verbose, "sample_ready", "Sample is ready.");
        log.Write(other, LogVerbosity.Verbose, "mounted", "Mounted.");
        Assert.That(output.Count(line => line.Contains("event=sample_ready")), Is.EqualTo(64));
        Assert.That(output.Count(line => line.Contains("event=temporary_log_suppressed")), Is.EqualTo(1));
        Assert.That(output[^1], Does.Contain("category=LogStorage"));
    }

    [Test]
    public void Write_BoundsStringsAndEscapesLineBreaksAndQuotes()
    {
        var categories = new LogCategories();
        var category = categories.Register("log_storage", null, "Storage", LogVerbosity.Info, true);
        categories.Freeze();
        string? output = null;
        var log = new DiagnosticLogger(categories, _ => new LogCategorySettings(true, LogVerbosity.Info), (_, line) => output = line);
        log.Write(category, LogVerbosity.Info, "sample_ready", "Quote: \"a\"\r\n", () => new[]
        {
            LogField.Of("id", new string('x', 140)), LogField.Of("enabled", true), LogField.Of("count", 4)
        }, 42, "InvalidOperationException");
        Assert.That(output, Does.Contain("event=sample_ready").And.Contain("operation_id=42").And.Contain("enabled=true count=4"));
        Assert.That(output, Does.Contain("\\\"a\\\"").And.Contain("\\u000d\\u000a").And.Not.Contain("\n"));
        Assert.That(output, Does.Contain(new string('x', 128) + "...").And.Not.Contain(new string('x', 129)));
    }

    [Test]
    public void Write_SwallowsPolicyFieldAndSinkFailures()
    {
        var categories = new LogCategories();
        var category = categories.Register("log_storage", null, "Storage", LogVerbosity.Info, true);
        categories.Freeze();
        var log = new DiagnosticLogger(categories, _ => throw new InvalidOperationException(), (_, _) => Assert.Fail());
        Assert.That(log.Write(category, LogVerbosity.Error, "failed", "Failure."), Is.False);
        log = new DiagnosticLogger(categories, _ => new LogCategorySettings(true, LogVerbosity.Info), (_, _) => throw new InvalidOperationException());
        Assert.That(log.Write(category, LogVerbosity.Error, "failed", "Failure.", () => throw new InvalidOperationException()), Is.False);
        Assert.That(log.Write(category, LogVerbosity.Error, "failed", "Failure."), Is.False);
    }
}
