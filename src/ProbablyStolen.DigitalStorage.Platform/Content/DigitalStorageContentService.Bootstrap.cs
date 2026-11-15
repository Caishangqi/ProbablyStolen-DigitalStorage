using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed partial class DigitalStorageContentService
{
    internal void Bootstrap(bool builtin)
    {
        EnsureOpen();
        _bootstrapStarted = true;
        foreach (var owner in _dataSources.Select(source => source.Owner).Distinct(StringComparer.Ordinal).ToArray())
            if (!_batches.Any(batch => batch.Owner == owner))
            {
                var batch = CreateBatch(owner);
                batch.Close();
                _batches.Add(batch);
            }
        var batches = _batches.OrderBy(batch => batch.Owner == "digital_storage" ? 0 : 1)
            .ThenBy(batch => batch.Owner, StringComparer.Ordinal).ToArray();
        try
        {
            RunOwners(batches, batch => batch.Declarations.AddRange(batch.Manager.StageDeclarations()));
            ValidateDeclarations(batches);
            var types = RegistryTypes(batches);
            RunOwners(batches, batch => batch.Entries.AddRange(batch.Manager.StageEntries(types)));
            RunOwners(batches, batch => batch.DataDeclarations.AddRange(batch.Manager.StageDataDeclarations()));
            ValidateDataDeclarations(batches);
            var codecs = DataCodecs(batches);
            foreach (var source in _dataSources.ToArray())
            {
                if (_invalidOwners.Contains(source.Owner)) continue;
                try { DecodeSource(source, batches.First(batch => batch.Owner == source.Owner), codecs); }
                catch (Exception error)
                {
                    Exclude(source.Owner, new InvalidOperationException("Content source failed: " + source.RootPath + " (" + source.Path + ")", error));
                }
            }
            ValidateCandidates(batches);
            var accepted = Live(batches).ToArray();
            if (builtin) ValidateBuiltinContent(accepted);
            _manager.PublishStaged(BuiltinDeclarations().Concat(accepted.SelectMany(batch => batch.Declarations)),
                accepted.SelectMany(batch => batch.Entries), DataCodecs(accepted).Values.Select(value => value.Declaration));
            foreach (var batch in accepted) batch.Manager.ReleaseStagedEntries();
            _unavailable = !builtin && accepted.Length == 0;
            var definitions = _manager.Get<ModuleBayDefinition>(Identifier.Parse(DefinitionRegistryId));
            var bindings = _unavailable ? null : _manager.Get<ModuleBayBinding>(Identifier.Parse(BindingRegistryId)).Entries
                .ToDictionary(entry => entry.Value.NativeItemId, entry => (entry.Value, definitions.GetRequired(entry.Value.DefinitionId)),
                    StringComparer.Ordinal);
            _frozen = true;
            _bindingsByItem = bindings;
        }
        catch
        {
            _unavailable = true;
            _bindingsByItem = null;
            _manager.Dispose();
            foreach (var batch in batches) batch.Dispose();
            throw;
        }
    }

    private IEnumerable<Batch> Live(IEnumerable<Batch> batches) => batches.Where(batch => !_invalidOwners.Contains(batch.Owner));

    private void RunOwners(IEnumerable<Batch> batches, Action<Batch> action)
    {
        foreach (var group in Live(batches).GroupBy(batch => batch.Owner, StringComparer.Ordinal).ToArray())
            try { foreach (var batch in group) action(batch); }
            catch (Exception error) { Exclude(group.Key, error); }
    }

    private void Exclude(string owner, Exception error)
    {
        if (_invalidOwners.Contains(owner)) return;
        InvalidateOwner(owner);
        _diagnostic($"Digital Storage: content from {owner} was excluded ({error.GetType().Name}: {error.Message}).");
    }

    private static RegistryManager.Declaration Declaration<T>(string id) =>
        new(Identifier.Parse(id), typeof(T), key => new Registry<T>(key));

    private static IEnumerable<RegistryManager.Declaration> BuiltinDeclarations()
    {
        yield return Declaration<ModuleBayDefinition>(DefinitionRegistryId);
        yield return Declaration<ModuleBayBinding>(BindingRegistryId);
        yield return Declaration<IModuleBayProvider>(ProviderRegistryId);
    }

    private Dictionary<Identifier, Type> RegistryTypes(IEnumerable<Batch> batches) => BuiltinDeclarations()
        .Concat(Live(batches).SelectMany(batch => batch.Declarations)).ToDictionary(value => value.Id, value => value.ValueType);

    private void ValidateDeclarations(IEnumerable<Batch> batches)
    {
        foreach (var group in Live(batches).GroupBy(batch => batch.Owner, StringComparer.Ordinal).ToArray())
        {
            var declarations = group.SelectMany(batch => batch.Declarations).ToArray();
            if (declarations.Any(value => value.Id.Namespace != group.Key || IsBuiltinRegistry(value.Id))
                || declarations.GroupBy(value => value.Id).Any(values => values.Count() > 1))
                Exclude(group.Key, new InvalidOperationException("A registry must be declared once in its owner's namespace."));
        }
    }

    private void ValidateDataDeclarations(IEnumerable<Batch> batches)
    {
        var types = RegistryTypes(batches);
        foreach (var group in Live(batches).GroupBy(batch => batch.Owner, StringComparer.Ordinal).ToArray())
        {
            var declarations = group.SelectMany(batch => batch.DataDeclarations).ToArray();
            if (declarations.Any(value => value.Id.Namespace != group.Key || IsBuiltinRegistry(value.Id)
                || !types.TryGetValue(value.Id, out var type) || type != value.ValueType)
                || declarations.GroupBy(value => value.Id).Any(values => values.Count() > 1))
                Exclude(group.Key, new InvalidOperationException("A data decoder must match one declared registry and value type."));
        }
    }

    private Dictionary<Identifier, (string Owner, RegistryManager.DataDeclaration Declaration)> DataCodecs(IEnumerable<Batch> batches)
    {
        var result = new Dictionary<Identifier, (string, RegistryManager.DataDeclaration)>
        {
            [Identifier.Parse(DefinitionRegistryId)] = (string.Empty, new RegistryManager.DataDeclaration(Identifier.Parse(DefinitionRegistryId),
                typeof(ModuleBayDefinition), json => ModuleBayJsonDecoder.DecodeContent(json, ModuleBayJsonKind.Definition).Definitions.Single())),
            [Identifier.Parse(BindingRegistryId)] = (string.Empty, new RegistryManager.DataDeclaration(Identifier.Parse(BindingRegistryId),
                typeof(ModuleBayBinding), json => ModuleBayJsonDecoder.DecodeBindingEntry(json)))
        };
        foreach (var batch in Live(batches))
            foreach (var declaration in batch.DataDeclarations) result.Add(declaration.Id, (batch.Owner, declaration));
        return result;
    }

    private static void AddData(Batch batch, Identifier registryId, Identifier? entryId, string json,
        IReadOnlyDictionary<Identifier, (string Owner, RegistryManager.DataDeclaration Declaration)> codecs)
    {
        if (!codecs.TryGetValue(registryId, out var codec)) throw new InvalidOperationException("No data decoder is available for registry: " + registryId);
        var value = codec.Declaration.Decode(json) ?? throw new InvalidOperationException("A registry decoder returned null: " + registryId);
        var key = entryId ?? (value is ModuleBayDefinition definition ? definition.Id
            : value is ModuleBayBinding binding ? BindingKey(binding) : throw new InvalidOperationException("A data entry requires an identifier."));
        batch.Entries.Add(new RegisterEvent.PendingEntry(registryId, key, codec.Declaration.ValueType, value, static _ => { }, static () => { }));
        if (codec.Owner.Length != 0) batch.DataOwners.Add(codec.Owner);
    }

    private static void DecodeSource(DataSource source, Batch batch,
        IReadOnlyDictionary<Identifier, (string Owner, RegistryManager.DataDeclaration Declaration)> codecs)
    {
        if (source.RegistryId.HasValue) AddData(batch, source.RegistryId.Value, source.EntryId, source.Json, codecs);
        else if (source.Kind == ModuleBayJsonKind.Definition) AddData(batch, Identifier.Parse(DefinitionRegistryId), null, source.Json, codecs);
        else
            foreach (var json in ModuleBayJsonDecoder.BindingEntries(source.Json))
                AddData(batch, Identifier.Parse(BindingRegistryId), null, json, codecs);
    }

    private void ValidateCandidates(Batch[] batches)
    {
        int previous;
        do
        {
            previous = _invalidOwners.Count;
            var types = RegistryTypes(batches);
            var used = new HashSet<(Identifier Registry, Identifier Entry)>();
            foreach (var group in Live(batches).GroupBy(batch => batch.Owner, StringComparer.Ordinal).ToArray())
            {
                var keys = new HashSet<(Identifier Registry, Identifier Entry)>();
                var error = group.SelectMany(batch => batch.Entries).FirstOrDefault(entry =>
                    !types.TryGetValue(entry.RegistryId, out var type) || type != entry.ValueType
                    || !keys.Add((entry.RegistryId, entry.EntryId)) || used.Contains((entry.RegistryId, entry.EntryId))
                    || !EntryIdentityMatches(entry));
                if (error != null) Exclude(group.Key, new InvalidOperationException("Duplicate, unavailable, or inconsistent registry entry: " + error.EntryId));
                else used.UnionWith(keys);
            }
            var live = Live(batches).ToArray();
            var definitions = live.SelectMany(batch => batch.Definitions).ToDictionary(value => value.Id);
            var providers = live.SelectMany(batch => batch.Providers).Select(value => value.Id).ToHashSet();
            foreach (var group in live.GroupBy(batch => batch.Owner, StringComparer.Ordinal))
            {
                var missingDecoderOwner = group.SelectMany(batch => batch.DataOwners).Any(owner => _invalidOwners.Contains(owner));
                var invalidDecoder = group.SelectMany(batch => batch.DataDeclarations).Any(value => !types.TryGetValue(value.Id, out var type) || type != value.ValueType);
                var invalidBinding = group.SelectMany(batch => batch.Bindings).FirstOrDefault(binding =>
                    !definitions.TryGetValue(binding.DefinitionId, out var definition) || !providers.Contains(binding.ProviderId)
                    || binding.MaximumStage.HasValue && binding.MaximumStage.Value >= definition.Stages.Count);
                if (missingDecoderOwner || invalidDecoder || invalidBinding != null)
                    Exclude(group.Key, new InvalidOperationException("Unresolved content reference in owner: " + group.Key));
            }
        } while (previous != _invalidOwners.Count);
    }

    private static bool EntryIdentityMatches(RegisterEvent.PendingEntry entry) =>
        entry.RegistryId == Identifier.Parse(DefinitionRegistryId) ? entry.Value is ModuleBayDefinition definition && entry.EntryId == definition.Id
        : entry.RegistryId == Identifier.Parse(BindingRegistryId) ? entry.Value is ModuleBayBinding binding && entry.EntryId == BindingKey(binding)
        : entry.RegistryId != Identifier.Parse(ProviderRegistryId) || entry.Value is IModuleBayProvider provider && entry.EntryId == provider.Id;

    internal sealed class ContentSource
    {
        internal ContentSource(string owner, string path, bool index, Identifier? registryId = null, Identifier? entryId = null)
        { Owner = owner; Path = path; Index = index; RegistryId = registryId; EntryId = entryId; }
        internal string Owner { get; }
        internal string Path { get; }
        internal bool Index { get; }
        internal Identifier? RegistryId { get; }
        internal Identifier? EntryId { get; }
    }

    private sealed class DataSource
    {
        internal DataSource(string owner, string rootPath, string path, string json, ModuleBayJsonKind? kind, Identifier? registryId = null, Identifier? entryId = null)
        { Owner = owner; RootPath = rootPath; Path = path; Json = json; Kind = kind; RegistryId = registryId; EntryId = entryId; }
        internal string Owner { get; }
        internal string RootPath { get; }
        internal string Path { get; }
        internal string Json { get; }
        internal ModuleBayJsonKind? Kind { get; }
        internal Identifier? RegistryId { get; }
        internal Identifier? EntryId { get; }
    }
}
