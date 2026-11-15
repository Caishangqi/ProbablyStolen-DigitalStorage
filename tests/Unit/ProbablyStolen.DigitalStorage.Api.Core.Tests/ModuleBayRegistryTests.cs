using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Api.Core.Tests;

[TestFixture]
public sealed class ModuleBayRegistryTests
{
    [Test]
    public void Identifier_ParseRequiresLowercaseNamespaceAndPath()
    {
        Assert.That(Identifier.Parse("digital_storage:module_bay/terminal").ToString(), Is.EqualTo("digital_storage:module_bay/terminal"));
        AssertThrows<FormatException>(() => Identifier.Parse("DigitalStorage:terminal"));
        AssertThrows<FormatException>(() => Identifier.Parse("digital_storage"));
        Assert.That(default(Identifier).GetHashCode(), Is.EqualTo(default(Identifier).GetHashCode()));
        AssertThrows<ArgumentException>(() => new Registry<int>(default));
    }

    [Test]
    public void ResourceKey_EqualityIncludesRegistryIdentity()
    {
        var entry = Identifier.Parse("sample:entry");
        var first = new ResourceKey<int>(Identifier.Parse("sample:first"), entry);
        var second = new ResourceKey<int>(Identifier.Parse("sample:second"), entry);
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(first.IsValid, Is.True);
    }

    [Test]
    public void RegistryBuilder_RejectsDuplicateAndReturnsOrdinalOrder()
    {
        var builder = RegistryBuilder<int>.Create(Identifier.Parse("sample:values"));
        builder.Add(Identifier.Parse("z_mod:last"), 2);
        builder.Add(Identifier.Parse("a_mod:first"), 1);
        AssertThrows<InvalidOperationException>(() => builder.Add(Identifier.Parse("a_mod:first"), 3));
        var registry = builder.Build();
        Assert.That(registry.Entries.Select(entry => entry.Key.ToString()), Is.EqualTo(new[] { "a_mod:first", "z_mod:last" }));
        Assert.That(registry.GetRequired(Identifier.Parse("z_mod:last")), Is.EqualTo(2));
        Assert.That(registry.GetValueOrDefault(Identifier.Parse("missing:value")), Is.EqualTo(0));
        registry.Freeze();
        AssertThrows<InvalidOperationException>(() => registry.Register(Identifier.Parse("sample:late"), 4));
    }

    [Test]
    public void RegistryManager_ExposesDeclaredIdsInOrdinalOrder()
    {
        using var manager = new RegistryManager();
        manager.NewRegistry += e =>
        {
            e.Create<string>(Identifier.Parse("z_mod:values"));
            e.Create<int>(Identifier.Parse("a_mod:values"));
        };
        var builder = RegistryBuilder<string>.Create(Identifier.Parse("sample:built"));
        builder.Add(Identifier.Parse("sample:entry"), "value");
        manager.NewRegistry += e => e.Create(builder);
        manager.CreateRegistries();
        Assert.That(manager.RegistryIds.Select(id => id.ToString()), Is.EqualTo(new[] { "a_mod:values", "sample:built", "z_mod:values" }));
        manager.RegisterEntries();
        Assert.That(manager.Get<string>(Identifier.Parse("sample:built")).GetRequired(Identifier.Parse("sample:entry")), Is.EqualTo("value"));
        manager.CloseCallbacks();
        AssertThrows<InvalidOperationException>(() => manager.Register += _ => { });
    }

    [Test]
    public void RegistryManager_ClosesRetainedPhaseEventObjects()
    {
        using var manager = new RegistryManager();
        var id = Identifier.Parse("sample:values");
        NewRegistryEvent? newRegistry = null;
        RegisterEvent? registration = null;
        DataPackRegistryEvent? data = null;
        manager.NewRegistry += e => { newRegistry = e; e.Create<string>(id); };
        manager.Register += e => registration = e;
        manager.DataPackRegistry += e => data = e;
        manager.CreateRegistries();
        manager.RegisterEntries();
        manager.RegisterDataRegistries();
        AssertThrows<InvalidOperationException>(() => newRegistry!.Create<int>(Identifier.Parse("sample:late")));
        AssertThrows<InvalidOperationException>(() => registration!.Register(id, Identifier.Parse("sample:late"), "late"));
        AssertThrows<InvalidOperationException>(() => data!.NewRegistry(id, text => text));
    }

    [Test]
    public void DeferredRegister_EvaluatesEachFactoryOnceAndResolvesThroughRegistry()
    {
        var calls = 0;
        using var manager = new RegistryManager();
        var registryId = Identifier.Parse("sample:values");
        manager.NewRegistry += e => e.Create<string>(registryId);
        var registration = DeferredRegister<string>.Create(manager, registryId);
        var holder = registration.Register("entry", () => { calls++; return "value"; });
        manager.CreateRegistries();
        manager.RegisterEntries();
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(holder.Value, Is.EqualTo("value"));
        AssertThrows<InvalidOperationException>(() => registration.Register("late", () => "late"));
        manager.Dispose();
        AssertThrows<ObjectDisposedException>(() => _ = holder.Value);
    }

    [Test]
    public void RegistryManager_FailedBatchPublishesNothingAndCannotRetry()
    {
        using var manager = new RegistryManager();
        var registryId = Identifier.Parse("sample:values");
        manager.NewRegistry += e => e.Create<string>(registryId);
        var failedHolder = DeferredRegister<string>.Create(manager, registryId);
        var holder = failedHolder.Register("first", () => "first");
        manager.Register += e => e.Register(registryId, Identifier.Parse("sample:first"), "duplicate");
        manager.CreateRegistries();
        AssertThrows<InvalidOperationException>(() => manager.RegisterEntries());
        AssertThrows<KeyNotFoundException>(() => manager.Get<string>(registryId));
        AssertThrows<InvalidOperationException>(() => _ = holder.Value);
        AssertThrows<InvalidOperationException>(() => manager.CreateRegistries());
    }

    [Test]
    public void DeferredRegister_FailedManagerInvalidatesExistingHolder()
    {
        using var manager = new RegistryManager();
        var registryId = Identifier.Parse("sample:values");
        manager.NewRegistry += e => e.Create<string>(registryId);
        var holder = DeferredRegister<string>.Create(manager, registryId).Register("entry", () => "value");
        manager.Register += _ => throw new InvalidOperationException("failed owner batch");
        manager.CreateRegistries();
        AssertThrows<InvalidOperationException>(() => manager.RegisterEntries());
        AssertThrows<InvalidOperationException>(() => _ = holder.Value);
    }

    [Test]
    public void RegistryManager_InvokesRegisteredDataDecoderAndFreezes()
    {
        using var manager = new RegistryManager();
        var registryId = Identifier.Parse("sample:data");
        manager.NewRegistry += e => e.Create<int>(registryId);
        manager.DataPackRegistry += e => e.NewRegistry(registryId, text => int.Parse(text));
        manager.CreateRegistries();
        manager.RegisterEntries();
        manager.RegisterDataRegistries();
        Assert.That(manager.Decode<int>(registryId, "42"), Is.EqualTo(42));
        Assert.That(manager.IsFrozen, Is.True);
        AssertThrows<InvalidOperationException>(() => manager.Get<int>(registryId).Register(Identifier.Parse("sample:late"), 1));
    }

    [Test]
    public void ModuleBayLayout_CountsOpenCoordinatesAndChecksInclusionAtFixedOrigin()
    {
        var baseLayout = new ModuleBayLayout(3, 2, new[] { "100", "010" });
        var expanded = new ModuleBayLayout(4, 2, new[] { "1000", "0100" });
        Assert.That(baseLayout.AvailableCells, Is.EqualTo(4));
        Assert.That(expanded.ContainsOpenCellsOf(baseLayout), Is.True);
        Assert.That(baseLayout.ContainsOpenCellsOf(expanded), Is.False);
        Assert.That(baseLayout.IsOpen(-1, 0), Is.False);
    }

    [Test]
    public void ModuleBayLayout_DerivesLargeDimensionsAndChecksAreaOverflow()
    {
        var layout = new ModuleBayLayout(256, 1, new[] { new string('0', 256) });
        Assert.That(layout.Width, Is.EqualTo(256));
        Assert.That(layout.Area, Is.EqualTo(256));
        Assert.That(layout.AvailableCells, Is.EqualTo(256));
        AssertThrows<ArgumentOutOfRangeException>(() => new ModuleBayLayout(int.MaxValue, 2, Array.Empty<string>()));
        AssertThrows<ArgumentOutOfRangeException>(() => new ModuleBayLayout(257, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => new ModuleBayLayout(256, 257));
    }

    [Test]
    public void ModuleBayLayout_RejectsMalformedRowsAndDimensions()
    {
        AssertThrows<ArgumentException>(() => new ModuleBayLayout(3, 2, new[] { "000" }));
        AssertThrows<ArgumentException>(() => new ModuleBayLayout(3, 1, new[] { "00x" }));
        AssertThrows<ArgumentOutOfRangeException>(() => new ModuleBayLayout(0, 1, new[] { "" }));
    }

    [Test]
    public void ModuleBayDefinition_RequiresContiguousUniqueMonotonicStagesAndStableIds()
    {
        var stage0 = new ModuleBayStage(0, "base", new ModuleBayLayout(2, 1, new[] { "10" }));
        var stage1 = new ModuleBayStage(1, "expanded", new ModuleBayLayout(3, 1, new[] { "100" }));
        var definition = new ModuleBayDefinition("sample_mod:bay", new[] { stage1, stage0 });
        Assert.That(definition.Stages.Select(stage => stage.Level), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(definition.Id, Is.EqualTo(Identifier.Parse("sample_mod:bay")));
        AssertThrows<ArgumentException>(() => new ModuleBayDefinition("invalid", new[] { stage0 }));
        AssertThrows<ArgumentException>(() => new ModuleBayDefinition("bad:gap", new[] { stage0, new ModuleBayStage(2, "next", stage1.Layout) }));
        AssertThrows<ArgumentException>(() => new ModuleBayDefinition("bad:shrink", new[] { stage0, new ModuleBayStage(1, "next", new ModuleBayLayout(2, 1, new[] { "11" })) }));
        AssertThrows<ArgumentException>(() => new ModuleBayStage(0, "Upper", stage0.Layout));
        AssertThrows<ArgumentException>(() => new ModuleBayDefinition("bad:too_many", Enumerable.Range(0, ModuleBayDefinition.MaximumStages + 1)
            .Select(index => new ModuleBayStage(index, "stage" + index, ModuleBayLayout.Rectangle(1, 1)))));
        AssertThrows<ArgumentException>(() => new ModuleBayDefinition("bad:area", Enumerable.Range(0, 5)
            .Select(index => new ModuleBayStage(index, "stage" + index, ModuleBayLayout.Rectangle(256, 256)))));
    }

    [Test]
    public void ModuleBayBinding_ValidatesStableIdsAndStageLimit()
    {
        var binding = new ModuleBayBinding("native.machine", "sample:bay", "sample:provider", 3);
        Assert.That(binding.DefinitionId, Is.EqualTo(Identifier.Parse("sample:bay")));
        Assert.That(binding.ProviderId, Is.EqualTo(Identifier.Parse("sample:provider")));
        AssertThrows<ArgumentException>(() => new ModuleBayBinding("native", "bad", "sample:provider"));
        AssertThrows<ArgumentOutOfRangeException>(() => new ModuleBayBinding("native", "sample:bay", "sample:provider", -1));
    }

    private static void AssertThrows<T>(Action action) where T : Exception => Assert.Throws<T>((Action)(() => action()));
}
