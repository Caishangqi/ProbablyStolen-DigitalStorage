using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Content;

namespace ProbablyStolen.DigitalStorage.Api.Core.Tests;

[TestFixture]
public sealed class PublishedModuleBayJsonTests
{
    private static readonly int[] Expected = { 26, 31, 41, 59, 73, 95, 113, 139, 161, 191, 255 };

    [Test]
    public void TerminalStages_MatchPublishedCountsAndPreservePriorCoordinates()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ProbablyStolen.DigitalStorage.Api.Core.Tests.Fixtures.terminal.json");
        Assert.That(stream, Is.Not.Null);
        using var document = JsonDocument.Parse(stream!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("format_version").GetInt32(), Is.EqualTo(1));
        var stages = root.GetProperty("stages");
        Assert.That(stages.GetArrayLength(), Is.EqualTo(Expected.Length));
        ModuleBayLayout? prior = null;
        for (var index = 0; index < stages.GetArrayLength(); index++)
        {
            var stage = stages[index];
            Assert.That(stage.GetProperty("level").GetInt32(), Is.EqualTo(index));
            var layout = stage.GetProperty("layout");
            var rows = layout.GetProperty("rows");
            var model = ModuleBayLayout.Pattern(rows.EnumerateArray().Select(row => row.GetString()!));
            Assert.That(model.AvailableCells, Is.EqualTo(Expected[index]), "stage " + index);
            if (prior != null) Assert.That(model.ContainsOpenCellsOf(prior), Is.True, "stage " + index);
            prior = model;
        }
    }
}
