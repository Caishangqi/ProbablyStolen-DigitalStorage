using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>Composition-owned content registries and module-bay facade.</summary>
internal sealed partial class DigitalStorageContentService : IDigitalStorageContentApi, IDigitalStorageContentBootstrap, IModuleBayApi, IDisposable
{
    internal const string DefinitionRegistryId = "digital_storage:module_bay_definition";
    internal const string BindingRegistryId = "digital_storage:module_bay_binding";
    internal const string ProviderRegistryId = "digital_storage:module_bay_provider";
    internal const string CustomerRegistryId = CustomerIdentity.RegistryId;
    private readonly RegistryManager _manager = new();
    private readonly List<Batch> _batches = new();
    private readonly List<Action> _bakeCallbacks = new();
    private bool _bootstrapStarted;
    private readonly HashSet<string> _invalidOwners = new(StringComparer.Ordinal);
    private readonly List<Action<GameItem, ModuleBayState>> _listeners = new();
    private readonly Action<string> _diagnostic;
    private bool _frozen;
    private bool _disposed;
    private bool _unavailable = true;
    // The published bindings and their definitions by native item id, or null until the freeze, after disposal and
    // whenever builtin content is unavailable. A published dictionary is never mutated; the field is only replaced with
    // null, so any thread may read it unlocked.
    private volatile Dictionary<string, (ModuleBayBinding Binding, ModuleBayDefinition Definition)>? _bindingsByItem;

    internal DigitalStorageContentService(Action<string>? diagnostic = null, StorageDiagnostics? storageDiagnostics = null)
    {
        _diagnostic = diagnostic ?? (_ => { });
        _storageDiagnostics = storageDiagnostics;
    }

    public int ApiMajorVersion => ApiVersion.Major;
    public int ApiMinorVersion => DigitalStorageContentKeys.ContentApiMinor;
    public IReadOnlyRegistry<ModuleBayDefinition> ModuleBays { get { EnsureReady(); return _manager.Get<ModuleBayDefinition>(Identifier.Parse(DefinitionRegistryId)); } }
    public IReadOnlyRegistry<ModuleBayBinding> ModuleBayBindings { get { EnsureReady(); return _manager.Get<ModuleBayBinding>(Identifier.Parse(BindingRegistryId)); } }
    public IReadOnlyRegistry<CustomerDefinition> Customers { get { EnsureReady(); return _manager.Get<CustomerDefinition>(Identifier.Parse(CustomerRegistryId)); } }
    public IReadOnlyRegistry<T> GetRegistry<T>(Identifier id)
    {
        EnsureReady();
        return _manager.Get<T>(id);
    }
    IDigitalStorageContentBootstrap IDigitalStorageContentApi.Bootstrap => this;
    public IModuleBayApi ModuleBaysApi => this;
    public bool IsFrozen => _frozen;

    public void RegisterBatch(string sourceModId, Action<IContentRegistrationBatch> register)
    {
        EnsureOpen();
        ValidateOwner(sourceModId);
        if (_invalidOwners.Contains(sourceModId)) throw new InvalidOperationException("Content owner is invalid: " + sourceModId);
        if (register == null) throw new ArgumentNullException(nameof(register));
        var batch = new Batch(sourceModId);
        try
        {
            register(batch);
            batch.Close();
            _batches.Add(batch);
        }
        catch
        {
            batch.Dispose();
            InvalidateOwner(sourceModId);
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="bake"/> with the frozen registry named by <paramref name="key"/> once the registries freeze.
    /// Builtin registries use it to put their entries into the game, so contributors only register. A failing callback
    /// is reported and does not undo the freeze.
    /// </summary>
    internal void AddBakeCallback<T>(ResourceKey<Registry<T>> key, Action<IReadOnlyRegistry<T>> bake)
    {
        EnsureOpen();
        if (bake == null) throw new ArgumentNullException(nameof(bake));
        _bakeCallbacks.Add(() => bake(_manager.Get<T>(key.EntryId)));
    }

    internal bool IsUnavailable => _unavailable;

    public IDisposable SubscribeLayoutChanged(Action<GameItem, ModuleBayState> listener)
    {
        EnsureAlive();
        if (listener == null) throw new ArgumentNullException(nameof(listener));
        _listeners.Add(listener);
        return new Subscription(() => _listeners.Remove(listener));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bindingsByItem = null;
        _manager.Dispose();
        foreach (var batch in _batches) batch.Dispose();
        _batches.Clear(); _bakeCallbacks.Clear(); _listeners.Clear();
    }

    private bool TryBinding(GameItem item, out ModuleBayBinding binding, out ModuleBayDefinition definition) =>
        TryBinding(item.identifier, out binding, out definition);

    /// <summary>Only exact item bindings are managed, so other mods' machines keep their native module bays.</summary>
    internal bool TryBinding(string nativeItemId, out ModuleBayBinding binding, out ModuleBayDefinition definition)
    {
        binding = null!; definition = null!;
        var bindings = _bindingsByItem;
        if (bindings == null || !bindings.TryGetValue(nativeItemId, out var found)) return false;
        (binding, definition) = found;
        return true;
    }
    private ModuleBayBinding FindBinding(GameItem item) { TryBinding(item, out var binding, out _); return binding; }
    private bool TryProvider(ModuleBayBinding binding, GameItem item, out IModuleBayProvider provider, out string? reason)
    {
        provider = null!; reason = null;
        var registry = _manager.Get<IModuleBayProvider>(Identifier.Parse(ProviderRegistryId));
        if (!registry.TryGetValue(binding.ProviderId, out provider!) || !provider.TryGetInventory(item, out _) || !provider.IsAvailable(item, out reason)) return false;
        return true;
    }
    private void Notify(GameItem item, ModuleBayState state) { foreach (var listener in _listeners.ToArray()) try { listener(item, state); } catch { } }
    private static bool IsBuiltinRegistry(Identifier id) => id == Identifier.Parse(DefinitionRegistryId)
        || id == Identifier.Parse(BindingRegistryId) || id == Identifier.Parse(ProviderRegistryId)
        || id == Identifier.Parse(CustomerRegistryId);
    private void InvalidateOwner(string owner)
    {
        if (owner == "digital_storage")
        {
            _unavailable = true;
            _bindingsByItem = null;
        }
        _invalidOwners.Add(owner);
        foreach (var batch in _batches.Where(batch => batch.Owner == owner)) batch.Dispose();
        _batches.RemoveAll(batch => batch.Owner == owner);
    }
    private static void ValidateOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '.')))
            throw new ArgumentException("A lowercase source mod id is required.", nameof(owner));
    }
    private void EnsureOpen() { EnsureAlive(); if (_bootstrapStarted) throw new InvalidOperationException("Content bootstrap is closed."); }
    private void EnsureReady() { EnsureAlive(); if (!_frozen || _unavailable) throw new InvalidOperationException("Content registries are unavailable."); }
    private static void ValidateBuiltinContent(IReadOnlyList<Batch> accepted)
    {
        var builtin = accepted.Where(batch => batch.Owner == "digital_storage").ToArray();
        if (builtin.Length == 0) throw new InvalidOperationException("Builtin content batch is missing.");
        var definitions = builtin.SelectMany(batch => batch.Values<ModuleBayDefinition>(DefinitionRegistryId)).ToDictionary(item => item.Id);
        foreach (var item in new[] { (Id: "digital_storage:terminal", Count: 11), (Id: "digital_storage:vanilla_standard", Count: 3) })
            if (!definitions.TryGetValue(Identifier.Parse(item.Id), out var definition) || definition.Stages.Count != item.Count)
                throw new InvalidOperationException("Builtin definition is missing or has an incorrect stage count: " + item.Id);
        var providers = builtin.SelectMany(batch => batch.Values<IModuleBayProvider>(ProviderRegistryId)).Select(provider => provider.Id).ToHashSet();
        foreach (var id in new[] { "digital_storage:native_standard", "digital_storage:cell" })
            if (!providers.Contains(Identifier.Parse(id))) throw new InvalidOperationException("Builtin provider is missing: " + id);
        var required = new Dictionary<string, (string Definition, string Provider, int Maximum)>(StringComparer.Ordinal)
        {
            ["digital_storage.storage_terminal"] = ("digital_storage:terminal", "digital_storage:native_standard", 10),
            ["digital_storage.storage_drive"] = ("digital_storage:vanilla_standard", "digital_storage:native_standard", 2),
            ["digital_storage.storage_network_controller"] = ("digital_storage:vanilla_standard", "digital_storage:native_standard", 2),
            ["digital_storage.item_storage_cell_1k"] = ("digital_storage:vanilla_standard", "digital_storage:cell", 0),
            ["digital_storage.item_storage_cell_4k"] = ("digital_storage:vanilla_standard", "digital_storage:cell", 0),
            ["digital_storage.item_storage_cell_16k"] = ("digital_storage:vanilla_standard", "digital_storage:cell", 1),
            ["digital_storage.item_storage_cell_64k"] = ("digital_storage:vanilla_standard", "digital_storage:cell", 1),
            ["digital_storage.item_storage_cell_256k"] = ("digital_storage:vanilla_standard", "digital_storage:cell", 2),
            ["digital_storage.item_cell_housing"] = ("digital_storage:vanilla_standard", "digital_storage:cell", 2)
        };
        foreach (var item in required)
        {
            var binding = builtin.SelectMany(batch => batch.Values<ModuleBayBinding>(BindingRegistryId)).SingleOrDefault(value => value.NativeItemId == item.Key);
            if (binding == null || binding.DefinitionId != Identifier.Parse(item.Value.Definition)
                || binding.ProviderId != Identifier.Parse(item.Value.Provider)
                || binding.MaximumStage != item.Value.Maximum)
                throw new InvalidOperationException("Builtin binding is missing or incorrect: " + item.Key);
        }
    }
    private void EnsureAlive() { if (_disposed) throw new ObjectDisposedException(nameof(DigitalStorageContentService)); }

    private sealed class Batch : IContentRegistrationBatch, IDisposable
    {
        private bool _closed;
        private bool _disposed;

        internal Batch(string owner)
        {
            Owner = owner;
            Registries = new RegistryRegistration(new RegistryManager());
        }

        internal string Owner { get; }
        internal RegistryRegistration Registries { get; }
        internal RegistryManager Manager => Registries.Manager;
        internal List<RegistryManager.Declaration> Declarations { get; } = new();
        internal List<RegisterEvent.PendingEntry> Entries { get; } = new();
        internal IEnumerable<T> Values<T>(string registryId) =>
            Entries.Where(entry => entry.RegistryId == Identifier.Parse(registryId)).Select(entry => (T)entry.Value);
        public RegistryRegistration PublicRegistries { get { EnsureOpen(); return Registries; } }
        RegistryRegistration IContentRegistrationBatch.Registries => PublicRegistries;
        internal void Close()
        {
            if (_closed) return;
            _closed = true;
            Registries.Close();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Registries.Close();
            Manager.Dispose();
            Entries.Clear(); Declarations.Clear();
        }
        private void EnsureOpen()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Batch));
            if (_closed) throw new InvalidOperationException("Content registration batch is closed.");
        }
    }
    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;
        internal Subscription(Action dispose) => _dispose = dispose;
        public void Dispose() { var action = _dispose; _dispose = null; action?.Invoke(); }
    }
}
