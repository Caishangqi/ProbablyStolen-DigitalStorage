using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ProbablyStolen.DigitalStorage.ArchitectureTests;

/// <summary>
/// Names of an integrated mod appear only in its plugin folder in Compat, and Compat never patches through attributes
/// or acts as the mod entry.
/// </summary>
[TestFixture]
public sealed class IntegrationBoundaryTests
{
    private const string CompatProject = "ProbablyStolen.DigitalStorage.Compat";

    /// <summary>Integrated mods: their id and code namespace, and the only folder allowed to name them.</summary>
    private static readonly (string ModId, string Namespace, string Folder)[] IntegratedMods =
    {
        ("customerstocking", "CustomerStocking.", "CustomerStocking")
    };

    private static string RepositoryRoot => typeof(IntegrationBoundaryTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "RepositoryRoot").Value!;

    [Test]
    public void IntegratedModNames_AppearOnlyInTheirPluginFolder()
    {
        var violations = ProductSources()
            .SelectMany(source => FindForeignNames(source.Path, File.ReadAllText(source.Path)))
            .ToArray();

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void Compat_DoesNotPatchThroughAttributes_OrImplementTheModEntry()
    {
        var violations = ProductSources()
            .Where(source => source.Project == CompatProject)
            .SelectMany(source => FindCompatViolations(source.Path, File.ReadAllText(source.Path)))
            .ToArray();

        Assert.That(violations, Is.Empty);
    }

    [TestCase("src/ProbablyStolen.DigitalStorage.Platform/Storage/Foo.cs", "var target = \"CustomerStocking.AutoStocker:Run\";")]
    [TestCase("src/ProbablyStolen.DigitalStorage.Common/Foo.cs", "const string Id = \"customerstocking\";")]
    [TestCase("src/ProbablyStolen.DigitalStorage.Compat/Shared/Foo.cs", "const string Id = \"CustomerStocking\";")]
    public void ForeignNameRule_FlagsNamesOutsideThePluginFolder(string path, string line)
    {
        Assert.That(FindForeignNames(Path.Combine(RepositoryRoot, path), line), Is.Not.Empty);
    }

    [Test]
    public void ForeignNameRule_AllowsThePluginFolder()
    {
        var path = Path.Combine(RepositoryRoot, "src", CompatProject, "CustomerStocking", "Foo.cs");

        Assert.That(FindForeignNames(path, "AccessTools.Method(\"CustomerStocking.AutoStocker:Run\")"), Is.Empty);
    }

    [TestCase("[HarmonyPatch(typeof(GraphUtils), \"TryAcceptAll\")]")]
    [TestCase("public sealed class Entry : IMod")]
    [TestCase("/// docs\n[HarmonyLib.HarmonyPatch]")]
    public void CompatRule_FlagsAttributePatchesAndModEntries(string line)
    {
        Assert.That(FindCompatViolations("Foo.cs", line), Is.Not.Empty);
    }

    [Test]
    public void CompatRule_IgnoresComments()
    {
        Assert.That(FindCompatViolations("Foo.cs", "/// never <c>[HarmonyPatch]</c> attributes\n// : IMod"), Is.Empty);
    }

    private static IEnumerable<string> FindForeignNames(string path, string text)
    {
        var relative = Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');
        foreach (var (modId, codeNamespace, folder) in IntegratedMods)
        {
            if (relative.StartsWith($"src/{CompatProject}/{folder}/", StringComparison.Ordinal)) continue;
            if (text.Contains(codeNamespace, StringComparison.Ordinal) ||
                Regex.IsMatch(text, $"\\b{Regex.Escape(modId)}\\b", RegexOptions.IgnoreCase))
            {
                yield return $"{relative} names {modId}";
            }
        }
    }

    private static IEnumerable<string> FindCompatViolations(string path, string text)
    {
        // Comments may explain the rule; only code counts.
        text = Regex.Replace(text, @"//[^\n]*", string.Empty);
        if (Regex.IsMatch(text, @"\[\s*(HarmonyLib\.)?HarmonyPatch(Attribute)?\b"))
            yield return $"{path} uses [HarmonyPatch]; the integration host applies Compat patches";
        if (Regex.IsMatch(text, @":\s*([\w.]+\s*,\s*)*IMod\b"))
            yield return $"{path} implements IMod; only the Mod assembly is the entry";
    }

    private static IEnumerable<(string Project, string Path)> ProductSources()
    {
        var src = Path.Combine(RepositoryRoot, "src");
        foreach (var project in Directory.GetDirectories(src))
        {
            foreach (var file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            {
                var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Contains("obj") || parts.Contains("bin")) continue;
                yield return (Path.GetFileName(project), file);
            }
        }
    }
}
