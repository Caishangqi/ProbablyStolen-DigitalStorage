using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Integration;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class CompatibilityDiagnosticsTests
{
    [Test]
    public void DecorationNotice_UsesCompatibilityLevelAndPreservesSafeDetails()
    {
        var categories = new LogCategories();
        var category = categories.Register("log_comp", "log_comp", "Compatibility", LogVerbosity.Verbose, true);
        categories.Freeze();
        var output = new List<string>();
        var settings = new LogCategorySettings(false, LogVerbosity.Info);
        var logger = new DiagnosticLogger(categories, _ => settings, (_, message) => output.Add(message));
        var diagnostics = new CompatibilityDiagnostics(logger, category);
        var notice = new IntegrationNotice(IntegrationSeverity.Warning, "digital_storage:test", "test", "1.0.0",
            IntegrationState.Incompatible, false, "decoration_overlay_unavailable", decorationId: "digital_storage:test/border");

        diagnostics.Report(notice);
        settings = new LogCategorySettings(true, LogVerbosity.Error);
        diagnostics.Report(notice);
        Assert.That(output, Is.Empty);

        settings = new LogCategorySettings(true, LogVerbosity.Warning);
        diagnostics.Report(notice);
        Assert.That(output, Has.Count.EqualTo(1));
        Assert.That(output[0], Does.Contain("event=integration_status"));
        Assert.That(output[0], Does.Contain("reason=\"decoration_overlay_unavailable\""));
        Assert.That(output[0], Does.Contain("decoration_id=\"digital_storage:test/border\""));
    }
}
