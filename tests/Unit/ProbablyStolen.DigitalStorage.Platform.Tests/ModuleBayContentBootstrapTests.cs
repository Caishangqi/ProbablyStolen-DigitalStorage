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
public sealed class ModuleBayContentBootstrapTests
{
    private static ModuleBayStage[] Stages(int count) => Enumerable.Range(0, count)
        .Select(level => new ModuleBayStage(level, "stage_" + level, ModuleBayLayout.Rectangle(1, 1))).ToArray();

    private static void Provider(IContentRegistrationBatch batch, string id)
    {
        var key = Identifier.Parse(id);
        DeferredRegister<IModuleBayProvider>.Create(batch.Registries, DigitalStorageRegistries.ModuleBayProvider, key.Namespace)
            .Register(key.Path, () => new MemoryProvider(id));
    }

    private static void Definition(IContentRegistrationBatch batch, string id, int stages = 1)
    {
        var key = Identifier.Parse(id);
        DeferredRegister<ModuleBayDefinition>.Create(batch.Registries, DigitalStorageRegistries.ModuleBayDefinition, key.Namespace)
            .Register(key.Path, () => new ModuleBayDefinition(id, Stages(stages)));
    }

    private static void Binding(IContentRegistrationBatch batch, string owner, string item, string definition, string provider,
        int? maximum = null, string? path = null) =>
        DeferredRegister<ModuleBayBinding>.Create(batch.Registries, DigitalStorageRegistries.ModuleBayBinding, owner)
            .Register(path ?? item, () => new ModuleBayBinding(item, definition, provider, maximum));

    [Test]
    public void Bootstrap_MissingBuiltinContentFailsClosed()
    {
        using var service = new DigitalStorageContentService();
        service.RegisterBatch("digital_storage", batch => Provider(batch, "digital_storage:cell"));
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
    public void Bootstrap_ExcludesAnOwnerBindingOneNativeItemTwice()
    {
        using var service = new DigitalStorageContentService();
        service.RegisterBatch("keys_mod", batch =>
        {
            Provider(batch, "keys_mod:provider");
            Definition(batch, "keys_mod:bay");
            Binding(batch, "keys_mod", "native.machine", "keys_mod:bay", "keys_mod:provider", path: "first");
            Binding(batch, "keys_mod", "native.machine", "keys_mod:bay", "keys_mod:provider", path: "second");
        });
        service.RegisterBatch("good_mod", batch =>
        {
            Provider(batch, "good_mod:provider");
            Definition(batch, "good_mod:bay");
            Binding(batch, "good_mod", "good.machine", "good_mod:bay", "good_mod:provider");
        });
        service.Bootstrap(false);
        Assert.That(service.ModuleBayBindings.Entries.Select(entry => entry.Value.NativeItemId), Is.EqualTo(new[] { "good.machine" }));
    }

    [Test]
    public void Bootstrap_ExcludesTheLaterOwnerBindingAnAlreadyBoundNativeItem()
    {
        using var service = new DigitalStorageContentService();
        foreach (var owner in new[] { "a_mod", "z_mod" })
            service.RegisterBatch(owner, batch =>
            {
                Provider(batch, owner + ":provider");
                Definition(batch, owner + ":bay");
                Binding(batch, owner, "shared.machine", owner + ":bay", owner + ":provider");
            });
        service.Bootstrap(false);
        Assert.That(service.ModuleBayBindings.Entries.Single().Value.DefinitionId, Is.EqualTo(Identifier.Parse("a_mod:bay")));
    }

    [Test]
    public void Bootstrap_ExcludesADefinitionRegisteredUnderAnotherId()
    {
        using var service = new DigitalStorageContentService();
        service.RegisterBatch("bad_mod", batch => DeferredRegister<ModuleBayDefinition>
            .Create(batch.Registries, DigitalStorageRegistries.ModuleBayDefinition, "bad_mod")
            .Register("entry", () => new ModuleBayDefinition("bad_mod:other", Stages(1))));
        service.RegisterBatch("good_mod", batch => Definition(batch, "good_mod:bay"));
        service.Bootstrap(false);
        Assert.That(service.ModuleBays.Entries.Select(entry => entry.Key.ToString()), Is.EqualTo(new[] { "good_mod:bay" }));
    }

    private static readonly (string Item, string Definition, string Provider, int Maximum)[] BuiltinBindings =
    {
        ("digital_storage.storage_terminal", "terminal", "native_standard", 10),
        ("digital_storage.storage_drive", "vanilla_standard", "native_standard", 2),
        ("digital_storage.storage_network_controller", "vanilla_standard", "native_standard", 2),
        ("digital_storage.io_port", "vanilla_standard", "native_standard", 2),
        ("digital_storage.item_storage_cell_1k", "vanilla_standard", "cell", 0),
        ("digital_storage.item_storage_cell_4k", "vanilla_standard", "cell", 0),
        ("digital_storage.item_storage_cell_16k", "vanilla_standard", "cell", 1),
        ("digital_storage.item_storage_cell_64k", "vanilla_standard", "cell", 1),
        ("digital_storage.item_storage_cell_256k", "vanilla_standard", "cell", 2),
        ("digital_storage.item_cell_housing", "vanilla_standard", "cell", 2)
    };

    private static void RegisterBuiltin(DigitalStorageContentService service, int index = -1, string mutation = "",
        int terminalCount = 11, int vanillaCount = 3, string missingProvider = "")
    {
        service.RegisterBatch("digital_storage", batch =>
        {
            foreach (var provider in new[] { "native_standard", "cell" })
                if (provider != missingProvider) Provider(batch, "digital_storage:" + provider);
            Definition(batch, "digital_storage:terminal", terminalCount);
            Definition(batch, "digital_storage:vanilla_standard", vanillaCount);
            for (var i = 0; i < BuiltinBindings.Length; i++)
            {
                if (i == index && mutation == "missing") continue;
                var entry = BuiltinBindings[i];
                var definition = i == index && mutation == "definition" ? (entry.Definition == "terminal" ? "vanilla_standard" : "terminal") : entry.Definition;
                var provider = i == index && mutation == "provider" ? (entry.Provider == "cell" ? "native_standard" : "cell") : entry.Provider;
                var maximum = i == index && mutation == "maximum" ? 0 : entry.Maximum;
                if (i == index && mutation == "maximum" && entry.Maximum == 0) maximum = 1;
                Binding(batch, "digital_storage", entry.Item, "digital_storage:" + definition, "digital_storage:" + provider, maximum);
            }
        });
    }

    [Test]
    public void BuiltinModuleBays_PublishTheShippedDefinitionsAndBindings()
    {
        using var service = new DigitalStorageContentService();
        service.RegisterBatch("digital_storage", batch => BuiltinModuleBays.Register(batch.Registries));
        service.Bootstrap(true);
        Assert.That(service.IsUnavailable, Is.False);
        var terminal = service.ModuleBays.GetRequired(Identifier.Parse("digital_storage:terminal"));
        Assert.That(terminal.Stages.Select(stage => stage.Layout.AvailableCells),
            Is.EqualTo(new[] { 26, 31, 41, 59, 73, 95, 113, 139, 161, 191, 255 }));
        for (var level = 1; level < terminal.Stages.Count; level++)
            Assert.That(terminal.Stages[level].Layout.ContainsOpenCellsOf(terminal.Stages[level - 1].Layout), Is.True, "stage " + level);
        Assert.That(service.ModuleBays.GetRequired(Identifier.Parse("digital_storage:vanilla_standard")).Stages, Has.Count.EqualTo(3));
        Assert.That(service.TryBinding("digital_storage.io_port", out var ioPort, out _), Is.True);
        Assert.That(ioPort!.DefinitionId, Is.EqualTo(Identifier.Parse("digital_storage:vanilla_standard")));
        Assert.That(ioPort.ProviderId, Is.EqualTo(Identifier.Parse("digital_storage:native_standard")));
        Assert.That(ioPort.MaximumStage, Is.EqualTo(2));
        foreach (var expected in BuiltinBindings)
        {
            Assert.That(service.TryBinding(expected.Item, out var binding, out _), Is.True, expected.Item);
            Assert.That(binding.DefinitionId, Is.EqualTo(Identifier.Parse("digital_storage:" + expected.Definition)));
            Assert.That(binding.ProviderId, Is.EqualTo(Identifier.Parse("digital_storage:" + expected.Provider)));
            Assert.That(binding.MaximumStage, Is.EqualTo(expected.Maximum));
        }
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
        Assert.That(service.GetRegistry<IModuleBayProvider>(DigitalStorageRegistries.ModuleBayProvider.EntryId).Entries, Has.Count.EqualTo(2));
    }

    [TestCase("vanilla.machine")]
    [TestCase("other_mod.machine")]
    public void Bootstrap_LeavesUnboundItemsUnmanaged(string nativeItemId)
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service);
        service.Bootstrap(true);
        Assert.That(service.TryBinding(nativeItemId, out _, out _), Is.False);
        Assert.That(service.TryBinding("digital_storage.storage_terminal", out var binding, out var definition), Is.True);
        Assert.That(binding.MaximumStage, Is.EqualTo(10));
        Assert.That(definition.Stages, Has.Count.EqualTo(11));
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

    private static void RegisterExternalMachine(DigitalStorageContentService service) =>
        service.RegisterBatch("test_mod", batch =>
        {
            Provider(batch, "test_mod:provider");
            Definition(batch, "test_mod:bay");
            Binding(batch, "test_mod", "test_mod.machine", "test_mod:bay", "test_mod:provider");
        });

    // Never invoked: these tests cover registration only, so no game customer is constructed.
    private static StoreClient NoCustomer() => throw new InvalidOperationException("Not invoked.");

    private static void Customer(IContentRegistrationBatch batch, string id, string? portrait = null)
    {
        var key = Identifier.Parse(id);
        DeferredRegister<CustomerDefinition>.Create(batch.Registries, DigitalStorageRegistries.Customer, key.Namespace)
            .Register(key.Path, () => new CustomerDefinition(NoCustomer, portrait));
    }

    [Test]
    public void Customers_PublishBuiltinAndExternalDefinitionsInIdentifierOrder()
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service);
        service.RegisterBatch("digital_storage", batch => Customer(batch, "digital_storage:data_scientist", "digital_storage:digital_scientist"));
        service.RegisterBatch("test_mod", batch =>
        {
            Customer(batch, "test_mod:courier_2", "test_mod:courier");
            Customer(batch, "test_mod:courier_1", "test_mod:courier");
        });
        service.Bootstrap(true);

        Assert.That(service.Customers.Entries.Select(entry => CustomerIdentity.ToNativeId(entry.Key)), Is.EqualTo(new[]
        {
            "digital_storage.data_scientist", "test_mod.courier_1", "test_mod.courier_2"
        }));
        Assert.That(service.Customers.Entries.Select(entry => entry.Value.Portrait).Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public void Customers_ExcludeAnOwnerRegisteringInAnotherNamespace()
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service);
        service.RegisterBatch("test_mod", batch =>
        {
            Customer(batch, "test_mod:courier");
            Customer(batch, "digital_storage:impostor");
        });
        service.Bootstrap(true);

        Assert.That(service.Customers.Entries, Is.Empty);
    }

    [Test]
    public void Customers_ExcludeAnOwnerRegisteringOneIdTwice()
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service);
        service.RegisterBatch("test_mod", batch =>
        {
            Customer(batch, "test_mod:courier");
            Customer(batch, "test_mod:courier");
        });
        service.Bootstrap(true);

        Assert.That(service.Customers.Entries, Is.Empty);
    }

    [Test]
    public void Customers_AreUnavailableBeforeTheFreeze()
    {
        using var service = new DigitalStorageContentService();
        Assert.Throws<InvalidOperationException>((Action)(() => _ = service.Customers));
    }

    [Test]
    public void BakeCallbacks_RunAfterTheFreezeWithTheFrozenRegistry()
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service);
        service.RegisterBatch("test_mod", batch => Customer(batch, "test_mod:courier"));
        IReadOnlyRegistry<CustomerDefinition>? baked = null;
        var frozenWhenBaked = false;
        service.AddBakeCallback(DigitalStorageRegistries.Customer, registry =>
        {
            baked = registry;
            frozenWhenBaked = service.IsFrozen;
        });
        Assert.That(baked, Is.Null);
        service.Bootstrap(true);

        Assert.That(frozenWhenBaked, Is.True);
        Assert.That(baked!.Entries.Select(entry => entry.Key.ToString()), Is.EqualTo(new[] { "test_mod:courier" }));
        Assert.Throws<InvalidOperationException>((Action)(() => service.AddBakeCallback(DigitalStorageRegistries.Customer, _ => { })));
    }

    [Test]
    public void BakeCallbacks_ReportAFailureAndStillRunTheOthers()
    {
        var diagnostics = new List<string>();
        using var service = new DigitalStorageContentService(diagnostics.Add);
        RegisterBuiltin(service);
        var ran = false;
        service.AddBakeCallback(DigitalStorageRegistries.Customer, _ => throw new InvalidOperationException("boom"));
        service.AddBakeCallback(DigitalStorageRegistries.Customer, _ => ran = true);
        service.Bootstrap(true);

        Assert.That(ran, Is.True);
        Assert.That(service.IsFrozen, Is.True);
        Assert.That(diagnostics, Has.Some.Contains("boom"));
    }

    [Test]
    public void BakeCallbacks_DoNotRunWhenBuiltinContentIsUnavailable()
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service, terminalCount: 10);
        var ran = false;
        service.AddBakeCallback(DigitalStorageRegistries.Customer, _ => ran = true);
        Assert.Throws<InvalidOperationException>((Action)(() => service.Bootstrap(true)));
        Assert.That(ran, Is.False);
    }

    [Test]
    public void IsManagedId_ManagesOnlyPublishedBindingsOnEveryThread()
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service);
        RegisterExternalMachine(service);
        Assert.That(service.IsManagedId("digital_storage.storage_terminal"), Is.False);
        Assert.That(service.IsManagedId("test_mod.machine"), Is.False);
        service.Bootstrap(true);
        var ids = new[] { "digital_storage.storage_terminal", "test_mod.machine", "vanilla.machine", "*" };
        var expected = new[] { true, true, false, false };
        Assert.That(ids.Select(service.IsManagedId), Is.EqualTo(expected));
        var offThread = System.Threading.Tasks.Task.Run(() => ids.Select(service.IsManagedId).ToArray()).Result;
        Assert.That(offThread, Is.EqualTo(expected));
        service.Dispose();
        Assert.That(service.IsManagedId("digital_storage.storage_terminal"), Is.False);
    }

    [Test]
    public void TryBinding_ResolvesTheSameItemsAsIsManagedId()
    {
        using var service = new DigitalStorageContentService();
        RegisterBuiltin(service);
        RegisterExternalMachine(service);
        service.Bootstrap(true);
        foreach (var entry in service.ModuleBayBindings.Entries)
        {
            Assert.That(service.IsManagedId(entry.Value.NativeItemId), Is.True);
            Assert.That(service.TryBinding(entry.Value.NativeItemId, out var binding, out _), Is.True);
            Assert.That(binding, Is.SameAs(entry.Value));
        }
    }

    [Test]
    public void IsManagedId_ManagesNothingWithoutBuiltinContent()
    {
        using var failed = new DigitalStorageContentService();
        RegisterBuiltin(failed, terminalCount: 10);
        RegisterExternalMachine(failed);
        Assert.Throws<InvalidOperationException>((Action)(() => failed.Bootstrap(true)));
        Assert.That(failed.IsManagedId("digital_storage.storage_terminal"), Is.False);
        Assert.That(failed.IsManagedId("test_mod.machine"), Is.False);
        Assert.That(failed.TryBinding("digital_storage.storage_terminal", out _, out _), Is.False);
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
    public void Bootstrap_RejectedOwnerInvalidatesItsHoldersAndClosesBootstrap()
    {
        using var service = new DigitalStorageContentService();
        var id = Identifier.Parse("failed_mod:values");
        DeferredHolder<CustomValue, CustomValue>? holder = null;
        var calls = 0;
        service.RegisterBatch("failed_mod", batch =>
        {
            batch.Registries.NewRegistry += e => e.Create<CustomValue>(id);
            holder = DeferredRegister<CustomValue>.Create(batch.Registries, id).Register("value", () => { calls++; return new CustomValue("value"); });
            Binding(batch, "failed_mod", "failed.item", "missing:definition", "missing:provider");
        });
        service.Bootstrap(false);
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
        service.RegisterBatch(bindingOwner, batch =>
            Binding(batch, bindingOwner, "foreign.machine", definitionOwner + ":bay", definitionOwner + ":provider"));
        service.RegisterBatch(definitionOwner, batch =>
        {
            Definition(batch, definitionOwner + ":bay");
            Provider(batch, definitionOwner + ":provider");
        });
        service.Bootstrap(false);
        Assert.That(service.ModuleBayBindings.Entries, Has.Count.EqualTo(1));
        Assert.That(service.ModuleBays.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void Bootstrap_PropagatesRejectedOwnerReferencesAndKeepsIndependentOwner()
    {
        using var service = new DigitalStorageContentService();
        service.RegisterBatch("a_mod", batch => Binding(batch, "a_mod", "a.machine", "z_mod:bay", "z_mod:provider"));
        service.RegisterBatch("z_mod", batch =>
        {
            Definition(batch, "z_mod:bay");
            Provider(batch, "z_mod:provider");
            Binding(batch, "z_mod", "z.machine", "missing:bay", "z_mod:provider");
        });
        service.RegisterBatch("good_mod", batch => Provider(batch, "good_mod:provider"));
        service.Bootstrap(false);
        Assert.That(service.ModuleBayBindings.Entries, Is.Empty);
        Assert.That(service.ModuleBays.Entries, Is.Empty);
        Assert.That(service.GetRegistry<IModuleBayProvider>(DigitalStorageRegistries.ModuleBayProvider.EntryId).Entries.Select(entry => entry.Key.ToString()),
            Is.EqualTo(new[] { "good_mod:provider" }));
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
    public void Bootstrap_PublishesTypedRegistriesAndDisposesSubscriptions()
    {
        using var service = new DigitalStorageContentService();
        IDigitalStorageContentApi api = service;
        RegisterExternalMachine(service);
        service.Bootstrap(false);
        Assert.That(api.Bootstrap.IsFrozen, Is.True);
        Assert.Throws<InvalidOperationException>((Action)(() => api.Bootstrap.RegisterBatch("late", _ => { })));
        Assert.That(service.ModuleBays.Entries, Has.Count.EqualTo(1));
        Assert.That(service.ModuleBayBindings.Entries, Has.Count.EqualTo(1));
        using var token = service.SubscribeLayoutChanged((_, _) => { });
        service.Dispose();
        Assert.Throws<ObjectDisposedException>((Action)(() => service.SubscribeLayoutChanged((_, _) => { })));
    }

    private sealed class CustomValue
    {
        internal CustomValue(string text) => Text = text;
        internal string Text { get; }
    }

    private sealed class MemoryProvider : IModuleBayProvider
    {
        private readonly Identifier _id;
        internal MemoryProvider(string id) => _id = Identifier.Parse(id);
        public Identifier Id => _id;
        public bool TryGetInventory(GameItem item, out GameGridInventory inventory) { inventory = null!; return false; }
        public bool IsAvailable(GameItem item, out string? refusalReason) { refusalReason = "memory-only"; return false; }
    }
}
