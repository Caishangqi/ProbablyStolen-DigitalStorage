using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Platform.Content;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
public sealed class ModuleBayJsonDecoderTests
{
    [Test]
    public void Decoder_RejectsUnknownPropertiesAndComments()
    {
        Assert.Throws<FormatException>((Action)(() => ModuleBayJsonDecoder.DecodeContent("{\"format_version\":1,\"id\":\"x:y\",\"stages\":[],\"extra\":1}", ModuleBayJsonKind.Definition)));
        Assert.Throws<FormatException>((Action)(() => ModuleBayJsonDecoder.DecodeContent("{/*comment*/\"format_version\":1,\"id\":\"x:y\",\"stages\":[]}", ModuleBayJsonKind.Definition)));
    }

    [Test]
    public void Decoder_RejectsResourceBoundsBeforeCreatingModels()
    {
        Assert.Throws<FormatException>((Action)(() => ModuleBayJsonDecoder.DecodeContent(
            "{\"format_version\":1,\"id\":\"x:large\",\"stages\":[{\"level\":0,\"key\":\"base\",\"layout\":{\"type\":\"rectangle\",\"width\":2147483647,\"height\":1}}]}", ModuleBayJsonKind.Definition)));
        Assert.Throws<FormatException>((Action)(() => ModuleBayJsonDecoder.DecodeContent(
            "{\"format_version\":1,\"id\":\"x:large\",\"stages\":[{\"level\":0,\"key\":\"base\",\"layout\":{\"type\":\"rectangle\",\"width\":257,\"height\":1}}]}", ModuleBayJsonKind.Definition)));
        var stages = string.Join(",", System.Linq.Enumerable.Range(0, 65).Select(level =>
            "{\"level\":" + level + ",\"key\":\"stage_" + level + "\",\"layout\":{\"type\":\"rectangle\",\"width\":1,\"height\":1}}"));
        Assert.Throws<FormatException>((Action)(() => ModuleBayJsonDecoder.DecodeContent(
            "{\"format_version\":1,\"id\":\"x:many\",\"stages\":[" + stages + "]}", ModuleBayJsonKind.Definition)));
        stages = string.Join(",", Enumerable.Range(0, 5).Select(level =>
            "{\"level\":" + level + ",\"key\":\"stage_" + level + "\",\"layout\":{\"type\":\"rectangle\",\"width\":256,\"height\":256}}"));
        Assert.Throws<FormatException>((Action)(() => ModuleBayJsonDecoder.DecodeContent(
            "{\"format_version\":1,\"id\":\"x:area\",\"stages\":[" + stages + "]}", ModuleBayJsonKind.Definition)));
    }

    [Test]
    public void Decoder_UsesRectangleAndPatternModels()
    {
        var rectangle = ModuleBayJsonDecoder.DecodeContent("{\"format_version\":1,\"id\":\"x:rectangle\",\"stages\":[{\"level\":0,\"key\":\"base\",\"layout\":{\"type\":\"rectangle\",\"width\":2,\"height\":3}}]}", ModuleBayJsonKind.Definition);
        var pattern = ModuleBayJsonDecoder.DecodeContent("{\"format_version\":1,\"id\":\"x:pattern\",\"stages\":[{\"level\":0,\"key\":\"base\",\"layout\":{\"type\":\"pattern\",\"rows\":[\"10\",\"00\"]}}]}", ModuleBayJsonKind.Definition);
        Assert.That(rectangle.Definitions[0].Stages[0].Layout.AvailableCells, Is.EqualTo(6));
        Assert.That(pattern.Definitions[0].Stages[0].Layout.AvailableCells, Is.EqualTo(3));
    }
}

[TestFixture]
public sealed class ModuleBayContentBootstrapTests
{
    [Test]
    public void Bootstrap_MissingBuiltinContentFailsClosed()
    {
        using var service = new DigitalStorageContentService();
        service.RegisterBatch("digital_storage", batch => batch.RegisterProvider(new MemoryProvider("digital_storage:cell")));
        Assert.Throws<InvalidOperationException>((Action)(() => service.Bootstrap(true)));
        Assert.That(service.IsFrozen, Is.False);
        Assert.That(service.IsUnavailable, Is.True);
        Assert.Throws<InvalidOperationException>((Action)(() => _ = service.ModuleBays));
    }

    [Test]
    public void Bootstrap_UsesPerBatchDeferredRegistriesAndClosesBatchHandle()
    {
        using var service = new DigitalStorageContentService();
        IDigitalStorageContentApi api = service;
        IContentRegistrationBatch? retained = null;
        RegistryRegistration? retainedRegistryScope = null;
        DeferredHolder<CustomValue, CustomValue>? holder = null;
        DeferredRegister<CustomValue>? deferred = null;
        var registryId = Identifier.Parse("test_mod:custom_values");
        api.Bootstrap.RegisterBatch("test_mod", batch =>
        {
            retained = batch;
            retainedRegistryScope = batch.Registries;
            batch.Registries.NewRegistry += e => e.Create<CustomValue>(registryId);
            deferred = DeferredRegister<CustomValue>.Create(batch.Registries, registryId);
            holder = deferred.Register("value", () => new CustomValue("ok"));
        });
        Assert.That(retained, Is.Not.Null);
        Assert.That(retainedRegistryScope!.IsOpen, Is.False);
        Assert.Throws<InvalidOperationException>((Action)(() => retainedRegistryScope.NewRegistry += _ => { }));
        Assert.Throws<InvalidOperationException>((Action)(() => DeferredRegister<CustomValue>.Create(retainedRegistryScope, registryId)));
        Assert.Throws<InvalidOperationException>((Action)(() => retained!.Registries.NewRegistry += _ => { }));
        Assert.Throws<InvalidOperationException>((Action)(() => deferred!.Register("late", () => new CustomValue("late"))));
        service.Bootstrap(false);
        Assert.That(holder!.Value.Text, Is.EqualTo("ok"));
        Assert.That(api.GetRegistry<CustomValue>(registryId).GetRequired(Identifier.Parse("test_mod:value")).Text, Is.EqualTo("ok"));
    }

    [Test]
    public void Bootstrap_InvalidOwnerDisposesAllBatchManagersAndHolders()
    {
        using var service = new DigitalStorageContentService();
        IDigitalStorageContentApi api = service;
        DeferredHolder<CustomValue, CustomValue>? first = null;
        DeferredHolder<CustomValue, CustomValue>? second = null;
        var registryId = Identifier.Parse("failed_mod:custom_values");
        api.Bootstrap.RegisterBatch("failed_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<CustomValue>(registryId);
            first = DeferredRegister<CustomValue>.Create(batch.Registries, registryId).Register("first", () => new CustomValue("first"));
        });
        api.Bootstrap.RegisterBatch("failed_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<CustomValue>(registryId);
            second = DeferredRegister<CustomValue>.Create(batch.Registries, registryId).Register("second", () => new CustomValue("second"));
        });
        service.Bootstrap(false);
        Assert.Throws<ObjectDisposedException>((Action)(() => _ = first!.Value));
        Assert.Throws<ObjectDisposedException>((Action)(() => _ = second!.Value));
        Assert.Throws<InvalidOperationException>((Action)(() => api.GetRegistry<CustomValue>(registryId)));
    }

    [Test]
    public void Bootstrap_UsesInjectiveNativeBindingKeys()
    {
        using var service = new DigitalStorageContentService();
        IDigitalStorageContentApi api = service;
        api.Bootstrap.RegisterBatch("keys_mod", batch =>
        {
            batch.RegisterProvider(new MemoryProvider("keys_mod:provider_a"));
            batch.RegisterProvider(new MemoryProvider("keys_mod:provider_b"));
            batch.RegisterDefinition(new ModuleBayDefinition("keys_mod:bay_a", new[] { new ModuleBayStage(0, "base", ModuleBayLayout.Rectangle(1, 1)) }));
            batch.RegisterDefinition(new ModuleBayDefinition("keys_mod:bay_b", new[] { new ModuleBayStage(0, "base", ModuleBayLayout.Rectangle(1, 1)) }));
            batch.RegisterBinding(new ModuleBayBinding("native.a_b", "keys_mod:bay_a", "keys_mod:provider_a"));
            batch.RegisterBinding(new ModuleBayBinding("native.a.b", "keys_mod:bay_b", "keys_mod:provider_b"));
        });
        service.Bootstrap(false);
        Assert.That(api.ModuleBayBindings.Entries, Has.Count.EqualTo(2));
    }

    private static readonly (string Item, string Definition, string Provider, int Maximum)[] BuiltinBindings =
    {
        ("digital_storage.storage_terminal", "terminal", "native_standard", 10),
        ("digital_storage.storage_drive", "vanilla_standard", "native_standard", 2),
        ("digital_storage.storage_network_controller", "vanilla_standard", "native_standard", 2),
        ("digital_storage.item_storage_cell_1k", "vanilla_standard", "cell", 0),
        ("digital_storage.item_storage_cell_4k", "vanilla_standard", "cell", 0),
        ("digital_storage.item_storage_cell_16k", "vanilla_standard", "cell", 1),
        ("digital_storage.item_storage_cell_64k", "vanilla_standard", "cell", 1),
        ("digital_storage.item_storage_cell_256k", "vanilla_standard", "cell", 2),
        ("digital_storage.item_cell_housing", "vanilla_standard", "cell", 2),
        ("*", "vanilla_standard", "native_standard", 2)
    };

    private static string Profile(string name, int count) =>
        "{\"format_version\":1,\"id\":\"digital_storage:" + name + "\",\"stages\":[" +
        string.Join(",", Enumerable.Range(0, count).Select(level =>
            "{\"level\":" + level + ",\"key\":\"stage_" + level + "\",\"layout\":{\"type\":\"rectangle\",\"width\":1,\"height\":1}}")) + "]}";

    private static void RegisterBuiltin(DigitalStorageContentService service, int index = -1, string mutation = "",
        int terminalCount = 11, int vanillaCount = 3, string missingProvider = "")
    {
        service.RegisterBatch("digital_storage", batch =>
        {
            foreach (var provider in new[] { "native_standard", "cell" })
                if (provider != missingProvider) batch.RegisterProvider(new MemoryProvider("digital_storage:" + provider));
            foreach (var profile in new[] { Profile("terminal", terminalCount), Profile("vanilla_standard", vanillaCount) })
                batch.RegisterDefinition(ModuleBayJsonDecoder.DecodeContent(profile, ModuleBayJsonKind.Definition).Definitions.Single());
            for (var i = 0; i < BuiltinBindings.Length; i++)
            {
                if (i == index && mutation == "missing") continue;
                var entry = BuiltinBindings[i];
                var definition = i == index && mutation == "definition" ? (entry.Definition == "terminal" ? "vanilla_standard" : "terminal") : entry.Definition;
                var provider = i == index && mutation == "provider" ? (entry.Provider == "cell" ? "native_standard" : "cell") : entry.Provider;
                var maximum = i == index && mutation == "maximum" ? 0 : entry.Maximum;
                if (i == index && mutation == "maximum" && entry.Maximum == 0) maximum = 1;
                batch.RegisterBinding(new ModuleBayBinding(entry.Item, "digital_storage:" + definition,
                    "digital_storage:" + provider, maximum, entry.Item == "*"));
            }
        });
    }

    [Test]
    public void Bootstrap_CompleteBuiltinMemoryBatchPublishesRegistries()
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service);
        service.Bootstrap(true);
        Assert.That(service.IsUnavailable, Is.False);
        Assert.That(service.ModuleBays.Entries, Has.Count.EqualTo(2));
        Assert.That(service.ModuleBayBindings.Entries, Has.Count.EqualTo(10));
        Assert.That(service.GetRegistry<IModuleBayProvider>(Identifier.Parse(DigitalStorageContentService.ProviderRegistryId)).Entries, Has.Count.EqualTo(2));
        foreach (var expected in BuiltinBindings)
        {
            var actual = service.ModuleBayBindings.Entries.Single(entry => entry.Value.NativeItemId == expected.Item).Value;
            Assert.That(actual.DefinitionId, Is.EqualTo(Identifier.Parse("digital_storage:" + expected.Definition)));
            Assert.That(actual.ProviderId, Is.EqualTo(Identifier.Parse("digital_storage:" + expected.Provider)));
            Assert.That(actual.MaximumStage, Is.EqualTo(expected.Maximum));
            Assert.That(actual.IsStandardMachineTagFallback, Is.EqualTo(expected.Item == "*"));
        }
    }

    private static IEnumerable<TestCaseData> InvalidBindings()
    {
        for (var i = 0; i < BuiltinBindings.Length; i++)
            foreach (var mutation in new[] { "missing", "definition", "provider", "maximum" })
                yield return new TestCaseData(i, mutation).SetName("BuiltinBinding_" + i + "_" + mutation);
    }

    [TestCaseSource(nameof(InvalidBindings))]
    public void Bootstrap_RejectsEachIncorrectBuiltinBinding(int index, string mutation)
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service, index, mutation);
        Assert.Throws<InvalidOperationException>((Action)(() => service.Bootstrap(true)));
        Assert.That(service.IsUnavailable, Is.True);
        Assert.Throws<InvalidOperationException>((Action)(() => _ = service.ModuleBays));
    }

    [TestCase(10, 3, "")]
    [TestCase(11, 2, "")]
    [TestCase(11, 3, "native_standard")]
    [TestCase(11, 3, "cell")]
    public void Bootstrap_RejectsMissingProvidersAndIncorrectProfileCounts(int terminalCount, int vanillaCount, string missingProvider)
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service, terminalCount: terminalCount, vanillaCount: vanillaCount, missingProvider: missingProvider);
        Assert.Throws<InvalidOperationException>((Action)(() => service.Bootstrap(true)));
        Assert.That(service.IsUnavailable, Is.True);
    }

    [Test]
    public void Decoder_RejectsFallbackBeforeExactBinding()
    {
        const string json = "{\"format_version\":1,\"bindings\":[{\"native_item_id\":\"*\",\"definition_id\":\"digital_storage:vanilla_standard\",\"provider_id\":\"digital_storage:native_standard\",\"standard_machine_tag_fallback\":true},{\"native_item_id\":\"item\",\"definition_id\":\"digital_storage:vanilla_standard\",\"provider_id\":\"digital_storage:native_standard\"}]}";
        Assert.Throws<FormatException>((Action)(() => ModuleBayJsonDecoder.DecodeContent(json, ModuleBayJsonKind.Bindings)));
    }

    [TestCase("digital_storage.storage_terminal", true)]
    [TestCase("digital_storage.storage_drive", true)]
    [TestCase("digital_storage.storage_network_controller", true)]
    [TestCase("digital_storage.item_storage_cell_1k", true)]
    [TestCase("digital_storage.item_cell_housing", true)]
    [TestCase("vanilla.machine", false)]
    public void BuiltinItemIds_ProtectOnlyKnownProductsWhenUnavailable(string id, bool expected)
    {
        Assert.That(DigitalStorageContentService.IsBuiltinItemId(id), Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Bootstrap_SharedCustomRegistryCombinesBatchesAndOwners(bool declarationLast)
    {
        using var service = new DigitalStorageContentService();
        var id = Identifier.Parse("z_mod:values");
        var calls = 0;
        DeferredHolder<CustomValue, CustomValue>? first = null;
        DeferredHolder<CustomValue, CustomValue>? second = null;
        DeferredHolder<CustomValue, CustomValue>? external = null;
        void Declare() => service.RegisterBatch("z_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<CustomValue>(id);
            first = DeferredRegister<CustomValue>.Create(batch.Registries, id).Register("first", () => { calls++; return new CustomValue("first"); });
        });
        if (!declarationLast) Declare();
        service.RegisterBatch("z_mod", batch => second = DeferredRegister<CustomValue>.Create(batch.Registries, id)
            .Register("second", () => { calls++; return new CustomValue("second"); }));
        service.RegisterBatch("a_mod", batch => external = DeferredRegister<CustomValue>.Create(batch.Registries, id, "a_mod")
            .Register("external", () => { calls++; return new CustomValue("external"); }));
        if (declarationLast) Declare();
        service.Bootstrap(false);
        var registry = service.GetRegistry<CustomValue>(id);
        Assert.That(registry.Entries, Has.Count.EqualTo(3));
        Assert.That(first!.Value, Is.SameAs(registry.GetRequired(Identifier.Parse("z_mod:first"))));
        Assert.That(second!.Value.Text, Is.EqualTo("second"));
        Assert.That(external!.Value.Text, Is.EqualTo("external"));
        Assert.That(calls, Is.EqualTo(3));
        Assert.That(registry is IDisposable, Is.False);
        service.Dispose();
        Assert.Throws<ObjectDisposedException>((Action)(() => _ = external.Value));
    }

    [Test]
    public void Bootstrap_RejectedOwnerNeverBindsHolderDuringDataPhase()
    {
        using var service = new DigitalStorageContentService();
        var id = Identifier.Parse("failed_mod:values");
        DeferredHolder<CustomValue, CustomValue>? holder = null;
        var unbound = false;
        var calls = 0;
        service.RegisterBatch("failed_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<CustomValue>(id);
            holder = DeferredRegister<CustomValue>.Create(batch.Registries, id).Register("value", () => { calls++; return new CustomValue("value"); });
            batch.Registries.DataPackRegistry += phase =>
            {
                try { _ = holder.Value; }
                catch (InvalidOperationException) { unbound = true; }
            };
            batch.RegisterBinding(new ModuleBayBinding("failed.item", "missing:definition", "missing:provider"));
        });
        service.Bootstrap(false);
        Assert.That(unbound, Is.True);
        Assert.That(calls, Is.EqualTo(1));
        Assert.Throws<ObjectDisposedException>((Action)(() => _ = holder!.Value));
        Assert.Throws<InvalidOperationException>((Action)(() => service.GetRegistry<CustomValue>(id)));
        Assert.Throws<InvalidOperationException>((Action)(() => service.Bootstrap(false)));
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Bootstrap_ResolvesForwardOwnerReferences(bool reverseNames)
    {
        using var service = new DigitalStorageContentService();
        var definitionOwner = reverseNames ? "a_mod" : "z_mod";
        var bindingOwner = reverseNames ? "z_mod" : "a_mod";
        service.RegisterBatch(bindingOwner, batch => batch.RegisterBinding(
            new ModuleBayBinding("foreign.machine", definitionOwner + ":bay", definitionOwner + ":provider")));
        service.RegisterBatch(definitionOwner, batch =>
        {
            batch.RegisterDefinition(new ModuleBayDefinition(definitionOwner + ":bay", new[] { new ModuleBayStage(0, "base", ModuleBayLayout.Rectangle(1, 1)) }));
            batch.RegisterProvider(new MemoryProvider(definitionOwner + ":provider"));
        });
        service.Bootstrap(false);
        Assert.That(service.ModuleBayBindings.Entries, Has.Count.EqualTo(1));
        Assert.That(service.ModuleBays.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void Bootstrap_PropagatesRejectedOwnerReferencesAndKeepsIndependentOwner()
    {
        using var service = new DigitalStorageContentService();
        service.RegisterBatch("a_mod", batch => batch.RegisterBinding(new ModuleBayBinding("a.machine", "z_mod:bay", "z_mod:provider")));
        service.RegisterBatch("z_mod", batch =>
        {
            batch.RegisterDefinition(new ModuleBayDefinition("z_mod:bay", new[] { new ModuleBayStage(0, "base", ModuleBayLayout.Rectangle(1, 1)) }));
            batch.RegisterProvider(new MemoryProvider("z_mod:provider"));
            batch.RegisterBinding(new ModuleBayBinding("z.machine", "missing:bay", "z_mod:provider"));
        });
        service.RegisterBatch("good_mod", batch => batch.RegisterProvider(new MemoryProvider("good_mod:provider")));
        service.Bootstrap(false);
        Assert.That(service.ModuleBayBindings.Entries, Is.Empty);
        Assert.That(service.ModuleBays.Entries, Is.Empty);
        Assert.That(service.GetRegistry<IModuleBayProvider>(Identifier.Parse(DigitalStorageContentService.ProviderRegistryId)).Entries.Select(entry => entry.Key.ToString()),
            Is.EqualTo(new[] { "good_mod:provider" }));
    }

    [Test]
    public void Bootstrap_KeyedJsonUsesDeclaredDecoderAfterCodeStaging()
    {
        using var service = new DigitalStorageContentService();
        var id = Identifier.Parse("z_mod:numbers");
        var calls = 0;
        var stillUnbound = false;
        DeferredHolder<int, int>? holder = null;
        service.SubmitJson("a_mod", id, Identifier.Parse("a_mod:answer"), "Data/answer.json");
        service.LoadSource(service.Sources.Single(), "42", (_, _) => throw new InvalidOperationException("No external reads"));
        service.RegisterBatch("z_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<int>(id);
            holder = DeferredRegister<int>.Create(batch.Registries, id).Register("code", () => 7);
            batch.Registries.DataPackRegistry += e => e.NewRegistry(id, json =>
            {
                calls++;
                try { _ = holder.Value; }
                catch (InvalidOperationException) { stillUnbound = true; }
                return int.Parse(json, System.Globalization.CultureInfo.InvariantCulture);
            });
        });
        Assert.That(calls, Is.Zero);
        service.Bootstrap(false);
        Assert.That(service.GetRegistry<int>(id).GetRequired(Identifier.Parse("a_mod:answer")), Is.EqualTo(42));
        Assert.That(holder!.Value, Is.EqualTo(7));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(stillUnbound, Is.True);
    }

    [Test]
    public void Bootstrap_DecoderFailureRejectsOnlySourceOwnerAndInvalidatesItsCodeHolder()
    {
        using var service = new DigitalStorageContentService();
        var id = Identifier.Parse("z_mod:numbers");
        DeferredHolder<int, int>? bad = null;
        service.RegisterBatch("z_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<int>(id);
            batch.Registries.DataPackRegistry += e => e.NewRegistry(id, json => int.Parse(json, System.Globalization.CultureInfo.InvariantCulture));
            DeferredRegister<int>.Create(batch.Registries, id).Register("good", () => 7);
        });
        service.RegisterBatch("bad_mod", batch => bad = DeferredRegister<int>.Create(batch.Registries, id, "bad_mod").Register("code", () => 99));
        service.SubmitJson("bad_mod", id, Identifier.Parse("bad_mod:data"), "Data/bad.json");
        service.LoadSource(service.Sources.Single(), "false", (_, _) => throw new InvalidOperationException("No external reads"));
        service.Bootstrap(false);
        Assert.That(service.GetRegistry<int>(id).Entries.Select(entry => entry.Key.ToString()), Is.EqualTo(new[] { "z_mod:good" }));
        Assert.Throws<ObjectDisposedException>((Action)(() => _ = bad!.Value));
    }

    [TestCase("missing")]
    [TestCase("type")]
    [TestCase("duplicate")]
    public void Bootstrap_InvalidSharedContributionDoesNotCorruptDeclaredRegistry(string failure)
    {
        using var service = new DigitalStorageContentService();
        var id = Identifier.Parse("a_mod:values");
        service.RegisterBatch("a_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<int>(id);
            DeferredRegister<int>.Create(batch.Registries, id).Register("good", () => 5);
        });
        DeferredHolder<int, int>? holder = null;
        service.RegisterBatch("z_mod", batch =>
        {
            holder = DeferredRegister<int>.Create(batch.Registries, id, "z_mod").Register("pending", () => 1);
            batch.Registries.Register += e =>
            {
                if (failure == "missing") e.Register(Identifier.Parse("missing:table"), Identifier.Parse("z_mod:bad"), 1);
                else if (failure == "type") e.Register(id, Identifier.Parse("z_mod:bad"), "wrong");
                else e.Register(id, Identifier.Parse("a_mod:good"), 2);
            };
        });
        service.Bootstrap(false);
        Assert.That(service.GetRegistry<int>(id).Entries.Select(entry => entry.Value), Is.EqualTo(new[] { 5 }));
        Assert.Throws<ObjectDisposedException>((Action)(() => _ = holder!.Value));
    }

    [Test]
    public void Bootstrap_DataCodecOwnerFailureExcludesDependentSourceOwner()
    {
        using var service = new DigitalStorageContentService();
        var id = Identifier.Parse("z_mod:numbers");
        service.RegisterBatch("z_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<int>(id);
            batch.Registries.DataPackRegistry += e => e.NewRegistry(id, json => int.Parse(json, System.Globalization.CultureInfo.InvariantCulture));
            batch.RegisterBinding(new ModuleBayBinding("z.machine", "missing:bay", "missing:provider"));
        });
        service.RegisterBatch("a_mod", batch => batch.RegisterProvider(new MemoryProvider("a_mod:provider")));
        service.SubmitJson("a_mod", id, Identifier.Parse("a_mod:data"), "Data/entry.json");
        service.LoadSource(service.Sources.Single(), "42", (_, _) => throw new InvalidOperationException("No external reads"));
        service.RegisterBatch("good_mod", batch => batch.RegisterProvider(new MemoryProvider("good_mod:provider")));
        service.Bootstrap(false);
        Assert.Throws<KeyNotFoundException>((Action)(() => service.GetRegistry<int>(id)));
        Assert.That(service.GetRegistry<IModuleBayProvider>(Identifier.Parse(DigitalStorageContentService.ProviderRegistryId)).Entries.Select(entry => entry.Key.ToString()),
            Is.EqualTo(new[] { "good_mod:provider" }));
    }

    private sealed class CustomValue
    {
        internal CustomValue(string text) => Text = text;
        internal string Text { get; }
    }

    private sealed class MemoryProvider : IModuleBayProvider
    {
        private readonly Identifier _id;
        internal MemoryProvider(string id = "test_mod:provider") => _id = Identifier.Parse(id);
        public Identifier Id => _id;
        public bool TryGetInventory(GameItem item, out GameGridInventory inventory) { inventory = null!; return false; }
        public bool IsAvailable(GameItem item, out string? refusalReason) { refusalReason = "memory-only"; return false; }
    }

    [Test]
    public void LoadSource_UsesInjectedReaderForIndexedMemorySources()
    {
        using var service = new DigitalStorageContentService();
        IDigitalStorageContentApi api = service;
        api.Bootstrap.RegisterBatch("memory_mod", batch => batch.RegisterProvider(new MemoryProvider("memory_mod:provider")));
        var index = "{\"format_version\":1,\"profiles\":[{\"path\":\"Data/bay.json\"}],\"bindings\":[{\"path\":\"Data/bindings.json\"}]}";
        var files = new System.Collections.Generic.Dictionary<string, string>
        {
            ["Data/bay.json"] = "{\"format_version\":1,\"id\":\"memory_mod:bay\",\"stages\":[{\"level\":0,\"key\":\"base\",\"layout\":{\"type\":\"rectangle\",\"width\":1,\"height\":1}}]}",
            ["Data/bindings.json"] = "{\"format_version\":1,\"bindings\":[{\"native_item_id\":\"memory.item\",\"definition_id\":\"memory_mod:bay\",\"provider_id\":\"memory_mod:provider\"}]}"
        };
        service.LoadSource("memory_mod", "Data/registries.json", index, true, (owner, path) =>
        {
            Assert.That(owner, Is.EqualTo("memory_mod"));
            return files[path];
        });
        service.Bootstrap(false);
        Assert.That(api.ModuleBays.Entries, Has.Count.EqualTo(1));
        Assert.That(api.ModuleBayBindings.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadSource_PreservesRootPathWhenAnIndexedSourceFails()
    {
        using var service = new DigitalStorageContentService();
        var index = "{\"format_version\":1,\"profiles\":[{\"path\":\"Data/missing.json\"}],\"bindings\":[]}";
        var error = Assert.Throws<InvalidOperationException>((Action)(() => service.LoadSource("memory_mod", "Data/registries.json", index, true, (_, _) => throw new FormatException("missing"))));
        Assert.That(error!.Message, Does.Contain("Data/registries.json"));
        Assert.That(error.InnerException!.Message, Does.Contain("Data/missing.json"));
        Assert.Throws<InvalidOperationException>((Action)(() => service.RegisterBatch("memory_mod", _ => { })));
    }

    [Test]
    public void Bootstrap_PublishesTypedRegistriesAndDisposesSubscriptions()
    {
        using var service = new DigitalStorageContentService();
        IDigitalStorageContentApi api = service;
        api.Bootstrap.RegisterBatch("test_mod", batch =>
        {
            batch.RegisterProvider(new MemoryProvider());
            batch.RegisterDefinition(new ModuleBayDefinition("test_mod:bay", new[]
            {
                new ModuleBayStage(0, "base", ModuleBayLayout.Rectangle(1, 1))
            }));
            batch.RegisterBinding(new ModuleBayBinding("test_mod.machine", "test_mod:bay", "test_mod:provider"));
        });
        service.Bootstrap(false);
        Assert.That(api.Bootstrap.IsFrozen, Is.True);
        Assert.Throws<InvalidOperationException>((Action)(() => api.Bootstrap.RegisterBatch("late", _ => { })));
        Assert.That(service.ModuleBays.Entries, Has.Count.EqualTo(1));
        Assert.That(service.ModuleBayBindings.Entries, Has.Count.EqualTo(1));
        using var token = service.SubscribeLayoutChanged((_, _) => { });
        service.Dispose();
        Assert.Throws<ObjectDisposedException>((Action)(() => service.SubscribeLayoutChanged((_, _) => { })));
    }
}
