using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using NUnit.Framework;

namespace ProbablyStolen.DigitalStorage.ArchitectureTests;

[TestFixture]
public sealed class ProjectGraphTests
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedReferences =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Common"] = new HashSet<string>(StringComparer.Ordinal),
            ["Client"] = new HashSet<string>(StringComparer.Ordinal) { "Common" },
            ["Platform.ProbablyStolen"] = new HashSet<string>(StringComparer.Ordinal) { "Common", "Client" },
            ["Mod"] = new HashSet<string>(StringComparer.Ordinal) { "Common", "Client", "Platform.ProbablyStolen" }
        };

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedCoreAssemblyReferences =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Common"] = new HashSet<string>(StringComparer.Ordinal) { "netstandard" },
            ["Client"] = new HashSet<string>(StringComparer.Ordinal) { "netstandard", "ProbablyStolen.DigitalStorage.Common" }
        };

    private static string RepositoryRoot => FindRepositoryRoot(AppContext.BaseDirectory);

    [Test]
    public void ProductProjects_DeclareOnlyApprovedReferences()
    {
        foreach (var module in AllowedReferences.Keys)
        {
            var document = XDocument.Load(GetProductProjectPath(module));
            Assert.DoesNotThrow(new Action(() => ValidateProductProject(document, module)), module);
        }
    }

    [TestCase("Common", "Client")]
    [TestCase("Common", "Platform.ProbablyStolen")]
    [TestCase("Common", "Mod")]
    [TestCase("Client", "Platform.ProbablyStolen")]
    [TestCase("Platform.ProbablyStolen", "Mod")]
    public void DependencyRule_RejectsForbiddenProjectEdge(string source, string target)
    {
        var references = AllowedReferences[source]
            .Append(target)
            .Select(module => new XElement("ProjectReference", new XAttribute("Include", $"../ProbablyStolen.DigitalStorage.{module}/ProbablyStolen.DigitalStorage.{module}.csproj")));
        var document = new XDocument(
            new XElement("Project",
                new XElement("PropertyGroup", new XElement("RequireProbablyStolenReferences", source is "Platform.ProbablyStolen" or "Mod" ? "true" : "false")),
                new XElement("ItemGroup", references)));

        var error = Assert.Throws<InvalidOperationException>(new Action(() => ValidateProductProject(document, source)));

        Assert.That(error!.Message, Does.Contain(source).And.Contain(target));
    }

    [TestCase("Common", "Assembly-CSharp")]
    [TestCase("Common", "Assembly-CSharp-firstpass")]
    [TestCase("Common", "UnityEngine")]
    [TestCase("Common", "UnityEngine.CoreModule")]
    [TestCase("Common", "0Harmony")]
    [TestCase("Common", "HarmonyLib")]
    [TestCase("Common", "Steamworks.NET")]
    [TestCase("Common", "ProbablyStolen.DigitalStorage.Client")]
    [TestCase("Client", "Unexpected.Runtime")]
    public void CompiledCoreAssembly_RejectsForbiddenReference(string module, string assemblyName)
    {
        using var fixture = CreateMetadataFixture(assemblyName);

        var error = Assert.Throws<InvalidOperationException>(new Action(() => ValidateCoreAssembly(fixture, module)));

        Assert.That(error!.Message, Does.Contain(module).And.Contain(assemblyName));
    }

    [TestCase("UnityEngine", "Transform")]
    [TestCase("", "IMod")]
    [TestCase("Steamworks", "SteamClient")]
    public void CompiledCoreAssembly_RejectsForbiddenType(string typeNamespace, string typeName)
    {
        using var fixture = CreateMetadataFixture("netstandard", typeNamespace, typeName);

        var error = Assert.Throws<InvalidOperationException>(new Action(() => ValidateCoreAssembly(fixture, "Common")));

        Assert.That(error!.Message, Does.Contain(typeName));
    }

    [TestCase("Common")]
    [TestCase("Client")]
    public void CompiledCoreAssembly_ContainsOnlyApprovedReferences(string module)
    {
        var configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        var outputDirectory = Environment.GetEnvironmentVariable("DIGITAL_STORAGE_OUTPUT_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = Path.Combine(RepositoryRoot, "artifacts");
        }
        else if (!Path.IsPathRooted(outputDirectory))
        {
            outputDirectory = Path.Combine(RepositoryRoot, outputDirectory);
        }

        var assemblyPath = Path.Combine(
            Path.GetFullPath(outputDirectory),
            "build",
            $"ProbablyStolen.DigitalStorage.{module}",
            configuration,
            "netstandard2.1",
            $"ProbablyStolen.DigitalStorage.{module}.dll");

        Assert.That(File.Exists(assemblyPath), Is.True, $"Build the core projects before running architecture checks: {assemblyPath}");

        using var stream = File.OpenRead(assemblyPath);
        Assert.DoesNotThrow(new Action(() => ValidateCoreAssembly(stream, module)));
    }

    private static void ValidateProductProject(XDocument document, string module)
    {
        var references = document
            .Descendants("ProjectReference")
            .Select(element => GetProjectModuleName((string?)element.Attribute("Include") ?? string.Empty))
            .ToHashSet(StringComparer.Ordinal);
        if (!references.SetEquals(AllowedReferences[module]))
        {
            throw new InvalidOperationException($"{module} has an invalid project graph: {string.Join(", ", references.OrderBy(value => value, StringComparer.Ordinal))}.");
        }

        if (document.Descendants("PackageReference").Any())
        {
            throw new InvalidOperationException($"{module} has an undeclared package dependency.");
        }

        var requiresGame = document.Descendants("RequireProbablyStolenReferences").SingleOrDefault()?.Value == "true";
        if (requiresGame != (module is "Platform.ProbablyStolen" or "Mod"))
        {
            throw new InvalidOperationException($"{module} has an invalid game reference requirement.");
        }
    }

    private static void ValidateCoreAssembly(Stream stream, string module)
    {
        using var image = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var metadata = image.GetMetadataReader();
        foreach (var reference in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(reference).Name);
            if (!AllowedCoreAssemblyReferences[module].Contains(name))
            {
                throw new InvalidOperationException($"{module} references forbidden assembly {name}.");
            }
        }

        foreach (var reference in metadata.TypeReferences)
        {
            var type = metadata.GetTypeReference(reference);
            var typeNamespace = metadata.GetString(type.Namespace);
            var typeName = metadata.GetString(type.Name);
            if (IsForbiddenGameType(typeNamespace, typeName))
            {
                throw new InvalidOperationException($"{module} references forbidden type {typeNamespace}.{typeName}.");
            }
        }
    }

    private static MemoryStream CreateMetadataFixture(string assemblyName, string? typeNamespace = null, string? typeName = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("DependencyFixture"), metadata.GetOrAddGuid(new Guid("8580f831-5cf7-4a24-b149-180e2df68699")), default, default);
        var reference = metadata.AddAssemblyReference(metadata.GetOrAddString(assemblyName), new Version(1, 0, 0, 0), default, default, 0, default);
        if (typeName is not null)
        {
            metadata.AddTypeReference(reference, metadata.GetOrAddString(typeNamespace ?? string.Empty), metadata.GetOrAddString(typeName));
        }

        var builder = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return new MemoryStream(image.ToArray(), writable: false);
    }

    private static string GetProductProjectPath(string module)
    {
        return Path.Combine(RepositoryRoot, "src", $"ProbablyStolen.DigitalStorage.{module}", $"ProbablyStolen.DigitalStorage.{module}.csproj");
    }

    private static string GetProjectModuleName(string include)
    {
        var normalized = include.Replace('\\', '/');
        var fileName = normalized.Split('/').Last();
        const string prefix = "ProbablyStolen.DigitalStorage.";
        const string suffix = ".csproj";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) || !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return fileName;
        }

        return fileName[prefix.Length..^suffix.Length];
    }

    private static bool IsForbiddenGameType(string typeNamespace, string typeName)
    {
        if (typeNamespace.Length == 0)
        {
            return typeName is "IMod" or "ModManifest" or "ModLog";
        }

        return typeNamespace is "UnityEngine" or "HarmonyLib" or "Steamworks" ||
               typeNamespace.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
               typeNamespace.StartsWith("HarmonyLib.", StringComparison.Ordinal) ||
               typeNamespace.StartsWith("Steamworks.", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalStorage.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate DigitalStorage.sln from the test output directory.");
    }
}
