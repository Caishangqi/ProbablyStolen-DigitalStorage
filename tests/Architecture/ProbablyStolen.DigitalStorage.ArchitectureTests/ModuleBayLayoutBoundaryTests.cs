using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ProbablyStolen.DigitalStorage.ArchitectureTests;

[TestFixture]
public sealed class ModuleBayLayoutBoundaryTests
{
    [Test]
    public void LayoutWarning_LeavesInventoryLocksAndSessionFailureAlone()
    {
        var source = ReadAdapter();
        var query = MethodBody(source, "private ModuleBayState GetState");
        var warning = MethodBody(source, "private void ReportLayoutMismatch");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(query, Does.Contain("ReportLayoutMismatch(").And.Contain("new ModuleBayState(true,"));
        Assert.That(warning, Does.Contain("(item.uniqueId, stage)").And.Contain("module_bay_layout_mismatch"));
        Assert.That(query + warning, Does.Not.Match(@"\b(?:Unavailable|LockInv|UnlockInv|SetShape)\s*\("));
        Assert.That(query + warning, Does.Not.Match(@"(?:\.Failure|overrideLockInsert|overrideLockRemove)\s*=(?!=)"));
    }

    [Test]
    public void Initialization_ReportsLayoutMismatchWithoutLocking()
    {
        var method = MethodBody(ReadAdapter(), "public ModuleBayState Initialize");
        var layoutCheck = Regex.Match(method, @"(?s)var layout =.*?(?=catch\s*\()");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(layoutCheck.Success, Is.True);
        Assert.That(layoutCheck.Value, Does.Contain("ReportLayoutMismatch(").And.Contain("new ModuleBayState(true,"));
        Assert.That(layoutCheck.Value, Does.Not.Match(@"\b(?:Unavailable|LockInv|UnlockInv)\s*\("));
    }

    [Test]
    public void Expansion_RequiresModulePlacementWithoutMatchingTheRegisteredLayout()
    {
        var source = ReadAdapter();
        var preflight = MethodBody(source, "private ModuleBayChangeResult CanExpand");
        var execution = MethodBody(source, "private ModuleBayChangeResult ExecuteExpansion");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(preflight, Does.Match(@"(?s)if\s*\(!ChildrenFit\(bay!, state\.Definition!\.Stage\(nextStage\)\.Layout\)\)\s*return ModuleBayChangeResult\.Refused\(.*?return ModuleBayChangeResult\.Changed\(nextStage\)"));
        Assert.That(execution, Does.Match(@"(?s)CanExpand\(item, out var state, out var bay\);\s*if\s*\(!check\.Succeeded\)\s*return check;.*?session\.Transaction\.Execute\("));
        Assert.That(preflight + execution, Does.Not.Match(@"\b(?:Matches|ReportLayoutMismatch)\s*\("));
    }

    [Test]
    public void Expansion_ChecksActualPlacementAndRestoresTheCapturedGrid()
    {
        var source = ReadAdapter();
        var capture = MethodBody(source, "public void Capture");
        var validation = MethodBody(source, "public void Validate");
        var restore = MethodBody(source, "public void Restore");
        var restoreShape = MethodBody(source, "private void RestoreShape");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(capture, Does.Contain("_originalShape = new GridShapeBuilder(_bay.inventoryShape)")
            .And.Contain("new GridShapeBuilder(child.modifiedShape)"));
        Assert.That(validation, Does.Match(@"(?s)contentWindow\?\.Validate\(\);.*?if \(!MatchesOriginalModules\(\)\s*\|\|\s*!InstalledModulesFit\(\)\)\s*throw"));
        Assert.That(validation, Does.Not.Match(@"\b(?:Matches|MatchesOriginalShape|ReportLayoutMismatch)\s*\("));
        Assert.That(restore, Does.Match(@"(?s)RestoreShape\(\);.*?contentWindow\?\.Validate\(\);.*?!MatchesOriginalShape\(\).*?!MatchesOriginalModules\(\).*?throw"));
        Assert.That(restore, Does.Not.Contain("InstalledModulesFit("));
        Assert.That(restoreShape, Does.Match(@"(?s)_bay\.SetShape\(.*?restored\.SetTransform\(.*?\.SetDataOutside\(.*?ForceRecomputeDynamicBackground\("));
    }

    private static string ReadAdapter()
    {
        var root = typeof(ModuleBayLayoutBoundaryTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepositoryRoot").Value!;
        return File.ReadAllText(Path.Combine(root, "src", "ProbablyStolen.DigitalStorage.Platform", "Content",
            "DigitalStorageContentService.ModuleBays.cs"));
    }

    private static string MethodBody(string source, string signature)
    {
        // Core tests do not load the native adapter. Inspect only the named method's source body.
        var method = Regex.Match(source, $@"(?ms)^(?<indent> +){Regex.Escape(signature)}\([^\n]*\).*?^\k<indent>\{{.*?^\k<indent>\}}");
        Assert.That(method.Success, Is.True, signature);
        return method.Value;
    }
}
