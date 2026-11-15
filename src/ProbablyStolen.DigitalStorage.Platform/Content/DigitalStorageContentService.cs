using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>Composition-owned content registries and module-bay facade.</summary>
internal sealed partial class DigitalStorageContentService : IDigitalStorageContentApi, IDigitalStorageContentBootstrap, IModuleBayApi, IDisposable
{
    internal const string DefinitionRegistryId = "digital_storage:module_bay_definition";
    internal const string BindingRegistryId = "digital_storage:module_bay_binding";
    internal const string ProviderRegistryId = "digital_storage:module_bay_provider";
    private readonly RegistryManager _manager = new();
    private readonly List<Batch> _batches = new();
    private readonly List<ContentSource> _sources = new();
    private readonly List<DataSource> _dataSources = new();
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

    internal DigitalStorageContentService(Action<string>? diagnostic = null)
    {
        _diagnostic = diagnostic ?? (_ => { });

    }

    public int ApiMajorVersion => ApiVersion.Major;
    public int ApiMinorVersion => DigitalStorageContentKeys.ContentApiMinor;
    public IReadOnlyRegistry<ModuleBayDefinition> ModuleBays { get { EnsureReady(); return _manager.Get<ModuleBayDefinition>(Identifier.Parse(DefinitionRegistryId)); } }
    public IReadOnlyRegistry<ModuleBayBinding> ModuleBayBindings { get { EnsureReady(); return _manager.Get<ModuleBayBinding>(Identifier.Parse(BindingRegistryId)); } }
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
        var batch = CreateBatch(sourceModId);
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

    public void SubmitJson(string sourceModId, string modRelativePath) => Submit(sourceModId, modRelativePath, false);
    public void SubmitIndex(string sourceModId, string modRelativePath) => Submit(sourceModId, modRelativePath, true);

    internal IReadOnlyList<ContentSource> Sources => _sources.ToArray();

    public void SubmitJson(string sourceModId, Identifier registryId, Identifier entryId, string modRelativePath)
    {
        if (!registryId.IsValid || !entryId.IsValid) throw new ArgumentException("A data source requires registry and entry identifiers.");
        Submit(sourceModId, modRelativePath, false, registryId, entryId);
    }

    internal void LoadSource(string owner, string path, string json, bool index)
        => LoadSource(owner, path, json, index, ModHelper.ReadModFileText);

    internal void LoadSource(string owner, string path, string json, bool index, Func<string, string, string> readSource)
        => LoadSource(new ContentSource(owner, path, index), json, readSource);

    internal void LoadSource(ContentSource source, string json) => LoadSource(source, json, ModHelper.ReadModFileText);

    internal void LoadSource(ContentSource source, string json, Func<string, string, string> readSource)
    {
        EnsureOpen();
        ValidateOwner(source.Owner);
        if (_invalidOwners.Contains(source.Owner)) throw new InvalidOperationException("Content owner is invalid: " + source.Owner);
        ModuleBayJsonDecoder.ValidatePath(source.Path);
        if (readSource == null) throw new ArgumentNullException(nameof(readSource));
        try
        {
            if (source.Index)
            {
                foreach (var entry in ModuleBayJsonDecoder.DecodeIndex(json))
                {
                    try
                    {
                        var text = readSource(source.Owner, entry.Path);
                        ModuleBayJsonDecoder.ValidateJson(text);
                        _dataSources.Add(new DataSource(source.Owner, source.Path, entry.Path, text,
                            entry.Kind == "bindings" ? ModuleBayJsonKind.Bindings : ModuleBayJsonKind.Definition));
                    }
                    catch (Exception error) { throw new InvalidOperationException("Content source failed: " + entry.Path, error); }
                }
            }
            else
            {
                ModuleBayJsonDecoder.ValidateJson(json);
                _dataSources.Add(new DataSource(source.Owner, source.Path, source.Path, json,
                    source.RegistryId.HasValue ? null : ModuleBayJsonDecoder.DetectKind(json), source.RegistryId, source.EntryId));
            }
        }
        catch (Exception error)
        {
            InvalidateOwner(source.Owner);
            throw new InvalidOperationException("Content source failed: " + source.Path, error);
        }
    }

    internal void DiscardOwner(string owner) => InvalidateOwner(owner);

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
        _batches.Clear(); _sources.Clear(); _dataSources.Clear(); _listeners.Clear();
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
    private static Identifier BindingKey(ModuleBayBinding binding)
    {
        var encoded = string.Concat(binding.NativeItemId.Select(character => ((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)));
        return Identifier.Parse("digital_storage:binding/utf16_" + encoded);
    }
    private static bool IsBuiltinRegistry(Identifier id) => id == Identifier.Parse(DefinitionRegistryId)
        || id == Identifier.Parse(BindingRegistryId) || id == Identifier.Parse(ProviderRegistryId);
    private Batch CreateBatch(string owner)
    {
        var batch = new Batch(owner);
        batch.Manager.Register += e => batch.PublishLists(e);
        return batch;
    }
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
        _sources.RemoveAll(source => source.Owner == owner);
        _dataSources.RemoveAll(source => source.Owner == owner);
    }
    private static void ValidateOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '.')))
            throw new ArgumentException("A lowercase source mod id is required.", nameof(owner));
    }
    private void Submit(string owner, string path, bool index, Identifier? registryId = null, Identifier? entryId = null)
    {
        EnsureOpen(); ValidateOwner(owner);
        if (_invalidOwners.Contains(owner)) throw new InvalidOperationException("Content owner is invalid: " + owner);
        ModuleBayJsonDecoder.ValidatePath(path);
        if (_sources.Any(s => s.Owner == owner && s.Path == path)) throw new InvalidOperationException("Duplicate JSON source path.");
        _sources.Add(new ContentSource(owner, path, index, registryId, entryId));
    }
    private void EnsureOpen() { EnsureAlive(); if (_bootstrapStarted) throw new InvalidOperationException("Content bootstrap is closed."); }
    private void EnsureReady() { EnsureAlive(); if (!_frozen || _unavailable) throw new InvalidOperationException("Content registries are unavailable."); }
    private static void ValidateBuiltinContent(IReadOnlyList<Batch> accepted)
    {
        var builtin = accepted.Where(batch => batch.Owner == "digital_storage").ToArray();
        if (builtin.Length == 0) throw new InvalidOperationException("Builtin content batch is missing.");
        var definitions = builtin.SelectMany(batch => batch.Definitions).ToDictionary(item => item.Id);
        foreach (var item in new[] { (Id: "digital_storage:terminal", Count: 11), (Id: "digital_storage:vanilla_standard", Count: 3) })
            if (!definitions.TryGetValue(Identifier.Parse(item.Id), out var definition) || definition.Stages.Count != item.Count)
                throw new InvalidOperationException("Builtin definition is missing or has an incorrect stage count: " + item.Id);
        var providers = builtin.SelectMany(batch => batch.Providers).Select(provider => provider.Id).ToHashSet();
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
            var binding = builtin.SelectMany(batch => batch.Bindings).SingleOrDefault(value => value.NativeItemId == item.Key);
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
        internal List<RegistryManager.DataDeclaration> DataDeclarations { get; } = new();
        internal List<RegisterEvent.PendingEntry> Entries { get; } = new();
        internal HashSet<string> DataOwners { get; } = new(StringComparer.Ordinal);
        private readonly List<ModuleBayDefinition> _definitions = new();
        private readonly List<ModuleBayBinding> _bindings = new();
        private readonly List<IModuleBayProvider> _providers = new();
        internal IEnumerable<ModuleBayDefinition> Definitions => Entries.Where(entry => entry.RegistryId == Identifier.Parse(DefinitionRegistryId)).Select(entry => (ModuleBayDefinition)entry.Value);
        internal IEnumerable<ModuleBayBinding> Bindings => Entries.Where(entry => entry.RegistryId == Identifier.Parse(BindingRegistryId)).Select(entry => (ModuleBayBinding)entry.Value);
        internal IEnumerable<IModuleBayProvider> Providers => Entries.Where(entry => entry.RegistryId == Identifier.Parse(ProviderRegistryId)).Select(entry => (IModuleBayProvider)entry.Value);
        public RegistryRegistration PublicRegistries { get { EnsureOpen(); return Registries; } }
        RegistryRegistration IContentRegistrationBatch.Registries => PublicRegistries;
        public void RegisterProvider(IModuleBayProvider provider) { EnsureOpen(); _providers.Add(provider ?? throw new ArgumentNullException(nameof(provider))); }
        public void RegisterDefinition(ModuleBayDefinition definition) { EnsureOpen(); _definitions.Add(definition ?? throw new ArgumentNullException(nameof(definition))); }
        public void RegisterBinding(ModuleBayBinding binding) { EnsureOpen(); _bindings.Add(binding ?? throw new ArgumentNullException(nameof(binding))); }
        internal void PublishLists(RegisterEvent registration)
        {
            foreach (var definition in _definitions) registration.Register(Identifier.Parse(DefinitionRegistryId), definition.Id, definition);
            foreach (var binding in _bindings) registration.Register(Identifier.Parse(BindingRegistryId), BindingKey(binding), binding);
            foreach (var provider in _providers) registration.Register(Identifier.Parse(ProviderRegistryId), provider.Id, provider);
        }
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
            _definitions.Clear(); _bindings.Clear(); _providers.Clear();
            Entries.Clear(); Declarations.Clear(); DataDeclarations.Clear(); DataOwners.Clear();
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
