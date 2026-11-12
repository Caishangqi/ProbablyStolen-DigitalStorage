using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace ProbablyStolen.DigitalStorage.ArchitectureTests;

/// <summary>
/// Checks the logical layers inside the compiled Common assembly. One assembly cannot stop a namespace from calling
/// another, so these rules are read from the real metadata: base types, interfaces, custom attributes, member
/// signatures, locals, catch clauses and every type, method, field and signature token in method bodies, including
/// compiler-generated closures.
/// </summary>
[TestFixture]
public sealed class CommonLayerTests
{
    private const string Root = "ProbablyStolen.DigitalStorage.Common";

    // Domain holds rules and values; Contracts and Ports are the interfaces built from them; Application composes all
    // three. Nothing below Application may depend on it.
    private static readonly (string Source, string Target)[] ForbiddenLayerEdges =
    {
        ("Domain", "Application"),
        ("Contracts", "Application"),
        ("Ports", "Application")
    };

    private static string GetBuildMetadata(string key) => typeof(CommonLayerTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value!;

    [Test]
    public void CompiledCommon_LowerLayersDoNotDependOnApplication()
    {
        var graph = ReadCommonGraph();

        var violations = FindViolations(graph, Root);

        Assert.That(violations, Is.Empty);
    }

    [TestCase("ByCall", "Calls")]
    [TestCase("ByCatch", "Catches")]
    [TestCase("ByAttribute", "Attributes")]
    public void Analyzer_SeesEachReferenceKindOnItsOwn(string source, string target)
    {
        const string fixtureRoot = "ProbablyStolen.DigitalStorage.ArchitectureTests.LayerFixture";
        using var stream = File.OpenRead(typeof(CommonLayerTests).Assembly.Location);
        using var image = new PEReader(stream);
        var graph = NamespaceGraph.Read(image, fixtureRoot);

        Assert.That(graph[$"{fixtureRoot}.Domain.{source}"], Does.Contain($"{fixtureRoot}.Application.{target}"));
        Assert.That(FindViolations(graph, fixtureRoot), Does.Contain(
            $"{fixtureRoot}.Domain.{source} -> {fixtureRoot}.Application.{target}"));
    }

    [Test]
    public void Analyzer_SeesSignatureAndBodyDependencies()
    {
        var graph = ReadCommonGraph();

        // Known edges keep the analyzer honest: a reader that saw nothing would pass the rule above trivially.
        Assert.That(graph[$"{Root}.Application.Storage"], Does.Contain($"{Root}.Domain.Storage"));
        Assert.That(graph[$"{Root}.Domain.Storage"], Does.Contain($"{Root}.Contracts.Storage"),
            "IStorageCell extends IRootStorage.");
        Assert.That(graph[$"{Root}.Application.Transfers"], Does.Contain($"{Root}.Contracts.Transfers"));
    }

    [TestCase("Domain.Storage", "Application.Storage")]
    [TestCase("Domain.Network", "Application.Queries")]
    [TestCase("Contracts.Transfers", "Application.Transfers")]
    [TestCase("Ports", "Application")]
    public void Rule_RejectsDependencyOnApplication(string source, string target)
    {
        var graph = new Dictionary<string, HashSet<string>>
        {
            [$"{Root}.{source}"] = new() { $"{Root}.{target}" }
        };

        Assert.That(FindViolations(graph, Root), Has.Count.EqualTo(1));
    }

    [TestCase("Application.Storage", "Domain.Storage")]
    [TestCase("Application.Storage", "Contracts.Storage")]
    [TestCase("Domain.Storage", "Contracts.Storage")]
    [TestCase("Contracts.Transfers", "Domain.Storage")]
    [TestCase("Ports", "Contracts.Transfers")]
    public void Rule_AllowsDependenciesTowardTheCore(string source, string target)
    {
        var graph = new Dictionary<string, HashSet<string>>
        {
            [$"{Root}.{source}"] = new() { $"{Root}.{target}" }
        };

        Assert.That(FindViolations(graph, Root), Is.Empty);
    }

    private static Dictionary<string, HashSet<string>> ReadCommonGraph()
    {
        var path = GetBuildMetadata("CommonTargetPath");
        Assert.That(File.Exists(path), Is.True, $"Build the core projects before running architecture checks: {path}");
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        return NamespaceGraph.Read(image, Root);
    }

    private static List<string> FindViolations(Dictionary<string, HashSet<string>> graph, string root)
    {
        var violations = new List<string>();
        foreach (var (source, targets) in graph)
        {
            foreach (var target in targets)
            {
                if (ForbiddenLayerEdges.Any(edge => IsInLayer(source, root, edge.Source) && IsInLayer(target, root, edge.Target)))
                    violations.Add($"{source} -> {target}");
            }
        }

        return violations;
    }

    private static bool IsInLayer(string typeNamespace, string root, string layer)
    {
        var prefix = $"{root}.{layer}";
        return typeNamespace == prefix || typeNamespace.StartsWith(prefix + ".", StringComparison.Ordinal);
    }

    /// <summary>Namespace-level dependencies between types defined in one assembly.</summary>
    private static class NamespaceGraph
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => code.Value);

        internal static Dictionary<string, HashSet<string>> Read(PEReader image, string rootPrefix)
        {
            var metadata = image.GetMetadataReader();
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var handle in metadata.TypeDefinitions)
            {
                var source = NamespaceOf(metadata, handle);
                if (!source.StartsWith(rootPrefix, StringComparison.Ordinal)) continue;
                if (!graph.TryGetValue(source, out var targets))
                    graph[source] = targets = new HashSet<string>(StringComparer.Ordinal);
                var collector = new Collector(metadata, targets);
                var type = metadata.GetTypeDefinition(handle);

                collector.AddType(type.BaseType);
                collector.AddAttributes(type.GetCustomAttributes());
                foreach (var implementation in type.GetInterfaceImplementations())
                    collector.AddType(metadata.GetInterfaceImplementation(implementation).Interface);
                foreach (var parameter in type.GetGenericParameters())
                    collector.AddConstraints(parameter);
                foreach (var field in type.GetFields())
                {
                    var definition = metadata.GetFieldDefinition(field);
                    definition.DecodeSignature(collector, null);
                    collector.AddAttributes(definition.GetCustomAttributes());
                }

                foreach (var property in type.GetProperties())
                    collector.AddAttributes(metadata.GetPropertyDefinition(property).GetCustomAttributes());
                foreach (var method in type.GetMethods())
                {
                    var definition = metadata.GetMethodDefinition(method);
                    definition.DecodeSignature(collector, null);
                    collector.AddAttributes(definition.GetCustomAttributes());
                    foreach (var parameter in definition.GetParameters())
                        collector.AddAttributes(metadata.GetParameter(parameter).GetCustomAttributes());
                    foreach (var parameter in definition.GetGenericParameters())
                        collector.AddConstraints(parameter);
                    if (definition.RelativeVirtualAddress != 0)
                        ScanBody(image.GetMethodBody(definition.RelativeVirtualAddress), collector, metadata);
                }
            }

            foreach (var targets in graph.Values)
                targets.RemoveWhere(target => !target.StartsWith(rootPrefix, StringComparison.Ordinal));
            foreach (var (source, targets) in graph)
                targets.Remove(source);
            return graph;
        }

        internal static string NamespaceOf(MetadataReader metadata, TypeDefinitionHandle handle)
        {
            var type = metadata.GetTypeDefinition(handle);
            var declaring = type.GetDeclaringType();
            return declaring.IsNil ? metadata.GetString(type.Namespace) : NamespaceOf(metadata, declaring);
        }

        private static void ScanBody(MethodBodyBlock body, Collector collector, MetadataReader metadata)
        {
            if (!body.LocalSignature.IsNil)
                metadata.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(collector, null);
            foreach (var region in body.ExceptionRegions)
                collector.AddType(region.CatchType);

            var reader = body.GetILReader();
            while (reader.RemainingBytes > 0)
            {
                short value = reader.ReadByte();
                if (value == 0xFE) value = unchecked((short)(0xFE00 | reader.ReadByte()));
                var code = OpCodesByValue[value];
                switch (code.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        reader.Offset += 1;
                        break;
                    case OperandType.InlineVar:
                        reader.Offset += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        reader.Offset += 8;
                        break;
                    case OperandType.InlineSwitch:
                        var count = reader.ReadInt32();
                        reader.Offset += count * 4;
                        break;
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                        collector.AddToken(reader.ReadInt32());
                        break;
                    case OperandType.InlineSig:
                        collector.AddStandaloneSignature(reader.ReadInt32());
                        break;
                    default:
                        reader.Offset += 4;
                        break;
                }
            }
        }
    }

    /// <summary>Records the namespace of every type defined in the scanned assembly that a signature or token names.</summary>
    private sealed class Collector : ISignatureTypeProvider<string, object?>
    {
        private readonly MetadataReader _metadata;
        private readonly HashSet<string> _targets;

        internal Collector(MetadataReader metadata, HashSet<string> targets)
        {
            _metadata = metadata;
            _targets = targets;
        }

        internal void AddType(EntityHandle handle)
        {
            if (handle.IsNil) return;
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    _targets.Add(NamespaceGraph.NamespaceOf(_metadata, (TypeDefinitionHandle)handle));
                    break;
                case HandleKind.TypeSpecification:
                    _metadata.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null);
                    break;
            }
        }

        internal void AddAttributes(CustomAttributeHandleCollection attributes)
        {
            foreach (var handle in attributes)
                AddToken(MetadataTokens.GetToken(_metadata.GetCustomAttribute(handle).Constructor));
        }

        internal void AddStandaloneSignature(int token)
        {
            var handle = (StandaloneSignatureHandle)MetadataTokens.EntityHandle(token);
            var signature = _metadata.GetStandaloneSignature(handle);
            if (signature.GetKind() == StandaloneSignatureKind.Method) signature.DecodeMethodSignature(this, null);
        }

        internal void AddConstraints(GenericParameterHandle handle)
        {
            foreach (var constraint in _metadata.GetGenericParameter(handle).GetConstraints())
                AddType(_metadata.GetGenericParameterConstraint(constraint).Type);
        }

        internal void AddToken(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                case HandleKind.TypeReference:
                case HandleKind.TypeSpecification:
                    AddType(handle);
                    break;
                case HandleKind.MethodDefinition:
                    AddType(_metadata.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType());
                    break;
                case HandleKind.FieldDefinition:
                    AddType(_metadata.GetFieldDefinition((FieldDefinitionHandle)handle).GetDeclaringType());
                    break;
                case HandleKind.MemberReference:
                    var member = _metadata.GetMemberReference((MemberReferenceHandle)handle);
                    AddType(member.Parent);
                    if (member.GetKind() == MemberReferenceKind.Method) member.DecodeMethodSignature(this, null);
                    else member.DecodeFieldSignature(this, null);
                    break;
                case HandleKind.MethodSpecification:
                    var specification = _metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                    AddToken(MetadataTokens.GetToken(specification.Method));
                    specification.DecodeSignature(this, null);
                    break;
            }
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            _targets.Add(NamespaceGraph.NamespaceOf(reader, handle));
            return string.Empty;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => string.Empty;

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
            return string.Empty;
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => string.Empty;
        public string GetSZArrayType(string elementType) => string.Empty;
        public string GetArrayType(string elementType, ArrayShape shape) => string.Empty;
        public string GetByReferenceType(string elementType) => string.Empty;
        public string GetPointerType(string elementType) => string.Empty;
        public string GetPinnedType(string elementType) => string.Empty;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => string.Empty;
        public string GetGenericMethodParameter(object? genericContext, int index) => string.Empty;
        public string GetGenericTypeParameter(object? genericContext, int index) => string.Empty;
        public string GetFunctionPointerType(MethodSignature<string> signature) => string.Empty;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => string.Empty;
    }
}
