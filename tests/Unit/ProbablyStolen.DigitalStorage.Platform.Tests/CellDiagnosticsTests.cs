using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
internal sealed class CellDiagnosticsTests
{
    [Test]
    public void Once_SeparatesCellFaultAndVerboseBudgetsWithoutOutputtingExceptionText()
    {
        var categories = new LogCategories();
        var storage = categories.Register("log_storage", null, "Storage", LogVerbosity.Verbose, true);
        categories.Freeze();
        var output = new List<string>();
        var log = new DiagnosticLogger(categories, _ => new LogCategorySettings(true, LogVerbosity.Verbose), (_, line) => output.Add(line));
        var diagnostics = new CellDiagnostics(log, storage);
        var cell = new object();
        for (var i = 0; i < 80; ++i)
            diagnostics.Once(new object(), LogVerbosity.Verbose, "cell_mounted", "Mounted.", Array.Empty<LogField>);
        for (var i = 0; i < 6; ++i)
            diagnostics.Once(cell, LogVerbosity.Error, "cell_read_failed", "Read failed.", Array.Empty<LogField>, new Exception("private " + i));
        diagnostics.Once(new object(), LogVerbosity.Error, "cell_read_failed", "Read failed.", Array.Empty<LogField>, new Exception("private 0"));
        Assert.That(output.Count(line => line.Contains("event=cell_mounted")), Is.EqualTo(64));
        Assert.That(output.Count(line => line.Contains("exception_type=")), Is.EqualTo(5));
        Assert.That(output.Count(line => line.Contains("Further distinct failures")), Is.EqualTo(1));
        Assert.That(output.Any(line => line.Contains("private")), Is.False);
    }
}
