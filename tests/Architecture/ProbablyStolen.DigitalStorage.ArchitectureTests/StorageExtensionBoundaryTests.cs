using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace ProbablyStolen.DigitalStorage.ArchitectureTests;

/// <summary>
/// The storage view reaches features only through its extension points: the storage presentation and extension
/// namespaces never depend on a feature's namespace, and the extension points stay out of the public API.
/// </summary>
[TestFixture]
public sealed class StorageExtensionBoundaryTests
{
    private const string ClientRoot = "ProbablyStolen.DigitalStorage.Client";
    private const string Stacking = ClientRoot + ".Terminal.Features.Stacking";
    private static readonly string[] Hosts = { ClientRoot + ".Terminal.Presentation", ClientRoot + ".Terminal.Extensions" };
    private static readonly string[] SharedInteractionHosts =
        { ClientRoot + ".Interaction.Drag", ClientRoot + ".Terminal.Presentation" };
    private static readonly string[] ExtensionTypes =
        { "IStorageEntryProjector", "ISlotDecorator", "IEntryTooltipContributor", "StoragePresentationPipeline" };
    private static readonly string[] SharedInteractionTypes =
    {
        "PointerGesture", "PointerGesturePhase", "PointerButton", "PointerPoint", "PointerRectangle",
        "DragSelectionCandidate`1", "DragSelectionModel`1", "DragAmountModel", "DragGhostView"
    };

    [Test]
    public void CompiledClient_StorageViewDoesNotDependOnTheStackingFeature()
    {
        var graph = ReadGraph("ClientTargetPath", ClientRoot);

        var violations = graph.Where(pair => Hosts.Any(host => InNamespace(pair.Key, host)))
            .SelectMany(pair => pair.Value.Where(target => InNamespace(target, Stacking)).Select(target => $"{pair.Key} -> {target}"))
            .ToArray();

        Assert.That(graph.Keys, Has.Some.EqualTo(Stacking), "The reader must see the feature namespace.");
        Assert.That(graph[Stacking], Does.Contain(ClientRoot + ".Terminal.Extensions"), "The feature implements the extension points.");
        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void CompiledClient_StorageViewAndDragPlanDoNotDependOnTheStackingFeature()
    {
        var graph = ReadGraph("ClientTargetPath", ClientRoot);

        var violations = graph.Where(pair => SharedInteractionHosts.Any(host => InNamespace(pair.Key, host)))
            .SelectMany(pair => pair.Value.Where(target => InNamespace(target, Stacking)).Select(target => $"{pair.Key} -> {target}"))
            .ToArray();

        Assert.That(graph.Keys, Has.Some.EqualTo(ClientRoot + ".Interaction.Drag"), "The reader must see shared drag types.");
        Assert.That(graph.Keys, Has.Some.EqualTo(ClientRoot + ".Terminal.Presentation"), "The reader must see the storage presentation.");
        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void CompiledClient_SharedInteractionTypesHaveNoGameOrUnityDependencies()
    {
        var path = GetMetadata("ClientTargetPath");
        Assert.That(File.Exists(path), Is.True, $"Build the core projects before running architecture checks: {path}");
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var names = metadata.TypeDefinitions.Select(handle => metadata.GetString(metadata.GetTypeDefinition(handle).Name)).ToArray();

        Assert.That(names, Has.Some.EqualTo("PointerGesture"), "The reader must see the shared gesture contract.");
        Assert.That(names, Has.Some.EqualTo("DragSelectionModel`1"), "The reader must see the shared selection model.");
        Assert.That(names, Has.Some.EqualTo("TerminalDragBatch"), "The reader must see the drag plan.");

        var forbiddenAssemblies = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Where(name => name.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                           name.StartsWith("Assembly-CSharp", StringComparison.Ordinal) ||
                           name.StartsWith("NeoForge", StringComparison.Ordinal) ||
                           name.StartsWith("NUnit", StringComparison.Ordinal) ||
                           name.Equals("Microsoft.NET.Test.Sdk", StringComparison.Ordinal))
            .ToArray();
        Assert.That(forbiddenAssemblies, Is.Empty);

        var forbiddenTypes = metadata.TypeReferences
            .Select(handle => metadata.GetTypeReference(handle))
            .Select(type => (Namespace: metadata.GetString(type.Namespace), Name: metadata.GetString(type.Name)))
            .Where(type => type.Namespace == "UnityEngine" || type.Namespace.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                           type.Namespace == "Steamworks" || type.Namespace.StartsWith("Steamworks.", StringComparison.Ordinal) ||
                           type.Namespace == "QFSW.QC" || type.Namespace.StartsWith("QFSW.QC.", StringComparison.Ordinal) ||
                           (type.Namespace.Length == 0 && type.Name is "IMod" or "ModManifest" or "ModLog"))
            .Select(type => $"{type.Namespace}.{type.Name}")
            .ToArray();
        Assert.That(forbiddenTypes, Is.Empty);
    }

    [Test]
    public void PlatformStorageViewSources_DoNotNameTheStackingFeature()
    {
        // The Platform assembly needs the game to build, so the core gate reads its storage view sources instead.
        var folder = Path.Combine(GetMetadata("RepositoryRoot"), "src", "ProbablyStolen.DigitalStorage.Platform", "Client",
            "Terminal", "Features", "StorageTerminal");
        var offenders = Directory.EnumerateFiles(folder, "*.cs")
            .Where(path => File.ReadAllText(path).Contains("Stacking", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToArray();

        Assert.That(Directory.EnumerateFiles(folder, "*.cs"), Is.Not.Empty);
        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void Rule_RejectsAStorageViewEdgeToTheFeature()
    {
        Assert.That(InNamespace(Stacking + ".Inner", Stacking), Is.True);
        Assert.That(Hosts.Any(host => InNamespace(ClientRoot + ".Terminal.Presentation", host)), Is.True);
        Assert.That(InNamespace(ClientRoot + ".Terminal.PresentationX", Hosts[0]), Is.False);
    }

    [Test]
    public void PlatformDragAdapters_DoNotNameTheStackingFeature()
    {
        var client = Path.Combine(GetMetadata("RepositoryRoot"), "src", "ProbablyStolen.DigitalStorage.Platform", "Client");
        var files = new[]
        {
            Path.Combine(client, "Drag", "DragGhostView.cs"),
            Path.Combine(client, "Terminal", "TerminalDragController.cs"),
            Path.Combine(client, "Terminal", "TerminalDragTargetResolver.cs"),
            Path.Combine(client, "Terminal", "TerminalSelectionBandView.cs")
        };

        Assert.That(files.All(File.Exists), Is.True, "The shared ghost and terminal drag adapters must exist.");
        var offenders = files.Where(path => File.ReadAllText(path).Contains("Stacking", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void PlatformStorageViewSources_DrawOnlyWhatFeaturesRegister()
    {
        // Overlay images and tooltip panel colors come from feature registrations; the view and the overlay layer hold
        // no feature image names or colors of their own.
        var client = Path.Combine(GetMetadata("RepositoryRoot"), "src", "ProbablyStolen.DigitalStorage.Platform", "Client");
        var files = Directory.EnumerateFiles(Path.Combine(client, "Terminal", "Features", "StorageTerminal"), "*.cs")
            .Append(Path.Combine(client, "SlotDecorationLayer.cs")).ToArray();
        var color = new System.Text.RegularExpressions.Regex(@"0x[0-9A-Fa-f]{6}\b|""#[0-9A-Fa-f]{3,6}""|new Color(32)?\(");
        var offenders = files.Where(path => File.Exists(path))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("annotation_square", StringComparison.Ordinal) || color.IsMatch(text);
            })
            .Select(Path.GetFileName).ToArray();

        Assert.That(files.All(File.Exists), Is.True, "The overlay layer and the storage view must exist.");
        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void ApiCore_DefinesNoSharedInteractionType()
    {
        using var stream = File.OpenRead(GetMetadata("Api.CoreTargetPath"));
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var names = metadata.TypeDefinitions.Select(handle => metadata.GetString(metadata.GetTypeDefinition(handle).Name)).ToArray();

        Assert.That(names.Intersect(SharedInteractionTypes), Is.Empty);
    }

    [Test]
    public void ApiCore_DefinesNoStorageExtensionPoint()
    {
        using var stream = File.OpenRead(GetMetadata("Api.CoreTargetPath"));
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var names = metadata.TypeDefinitions.Select(handle => metadata.GetString(metadata.GetTypeDefinition(handle).Name)).ToArray();
        var references = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();

        Assert.That(names, Has.Some.EqualTo("ItemKey"), "The reader must see Api.Core types.");
        Assert.That(names.Intersect(ExtensionTypes), Is.Empty);
        Assert.That(references, Has.None.EqualTo(ClientRoot));
    }

    private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> ReadGraph(
        string targetKey, string root)
    {
        var path = GetMetadata(targetKey);
        Assert.That(File.Exists(path), Is.True, $"Build the core projects before running architecture checks: {path}");
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        return CommonLayerTests.NamespaceGraph.Read(image, root);
    }

    private static bool InNamespace(string typeNamespace, string prefix) =>
        typeNamespace == prefix || typeNamespace.StartsWith(prefix + ".", StringComparison.Ordinal);

    private static string GetMetadata(string key) => typeof(StorageExtensionBoundaryTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value!;
}
