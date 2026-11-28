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
        var batches = _batches.OrderBy(batch => batch.Owner == "digital_storage" ? 0 : 1)
            .ThenBy(batch => batch.Owner, StringComparer.Ordinal).ToArray();
        try
        {
            RunOwners(batches, batch => batch.Declarations.AddRange(batch.Manager.StageDeclarations()));
            ValidateDeclarations(batches);
            var types = RegistryTypes(batches);
            RunOwners(batches, batch => batch.Entries.AddRange(batch.Manager.StageEntries(types)));
            ValidateCandidates(batches);
            var accepted = Live(batches).ToArray();
            if (builtin) ValidateBuiltinContent(accepted);
            _manager.PublishStaged(BuiltinDeclarations().Concat(accepted.SelectMany(batch => batch.Declarations)),
                accepted.SelectMany(batch => batch.Entries));
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

        if (_unavailable) return;
        foreach (var bake in _bakeCallbacks)
        {
            try { bake(); }
            catch (Exception error) { _diagnostic($"Digital Storage: a registry bake callback failed ({error.GetType().Name}: {error.Message})."); }
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

    private static RegistryManager.Declaration Declaration<T>(ResourceKey<Registry<T>> key) =>
        new(key.EntryId, typeof(T), id => new Registry<T>(id));

    private static IEnumerable<RegistryManager.Declaration> BuiltinDeclarations()
    {
        yield return Declaration(DigitalStorageRegistries.ModuleBayDefinition);
        yield return Declaration(DigitalStorageRegistries.ModuleBayBinding);
        yield return Declaration(DigitalStorageRegistries.ModuleBayProvider);
        yield return Declaration(DigitalStorageRegistries.Customer);
        yield return Declaration(DigitalStorageRegistries.VolatileItemFeature);
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

    private void ValidateCandidates(Batch[] batches)
    {
        int previous;
        do
        {
            previous = _invalidOwners.Count;
            var types = RegistryTypes(batches);
            var used = new HashSet<(Identifier Registry, Identifier Entry)>();
            var boundItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in Live(batches).GroupBy(batch => batch.Owner, StringComparer.Ordinal).ToArray())
            {
                var keys = new HashSet<(Identifier Registry, Identifier Entry)>();
                var entries = group.SelectMany(batch => batch.Entries).ToArray();
                var error = entries.FirstOrDefault(entry =>
                    !types.TryGetValue(entry.RegistryId, out var type) || type != entry.ValueType
                    || !keys.Add((entry.RegistryId, entry.EntryId)) || used.Contains((entry.RegistryId, entry.EntryId))
                    || !EntryIdentityMatches(entry, group.Key));
                // A native item has one module bay, and binding entry ids do not name the item, so check it directly.
                var items = group.SelectMany(batch => batch.Values<ModuleBayBinding>(BindingRegistryId)).Select(binding => binding.NativeItemId).ToArray();
                if (error == null && (items.Distinct(StringComparer.Ordinal).Count() != items.Length || items.Any(boundItems.Contains)))
                    Exclude(group.Key, new InvalidOperationException("A native item has more than one module bay binding."));
                else if (error != null) Exclude(group.Key, new InvalidOperationException("Duplicate, unavailable, or inconsistent registry entry: " + error.EntryId));
                else
                {
                    used.UnionWith(keys);
                    boundItems.UnionWith(items);
                }
            }
            var live = Live(batches).ToArray();
            var definitions = live.SelectMany(batch => batch.Values<ModuleBayDefinition>(DefinitionRegistryId)).ToDictionary(value => value.Id);
            var providers = live.SelectMany(batch => batch.Values<IModuleBayProvider>(ProviderRegistryId)).Select(value => value.Id).ToHashSet();
            foreach (var group in live.GroupBy(batch => batch.Owner, StringComparer.Ordinal))
            {
                var invalidBinding = group.SelectMany(batch => batch.Values<ModuleBayBinding>(BindingRegistryId)).FirstOrDefault(binding =>
                    !definitions.TryGetValue(binding.DefinitionId, out var definition) || !providers.Contains(binding.ProviderId)
                    || binding.MaximumStage.HasValue && binding.MaximumStage.Value >= definition.Stages.Count);
                if (invalidBinding != null)
                    Exclude(group.Key, new InvalidOperationException("Unresolved content reference in owner: " + group.Key));
            }
        } while (previous != _invalidOwners.Count);
    }

    /// <summary>
    /// Definitions and providers carry their own id, which must be their entry id. A customer becomes a game-wide
    /// catalog id, so one mod cannot register customers in another's namespace.
    /// </summary>
    private static bool EntryIdentityMatches(RegisterEvent.PendingEntry entry, string owner) =>
        entry.RegistryId == Identifier.Parse(DefinitionRegistryId) ? entry.Value is ModuleBayDefinition definition && entry.EntryId == definition.Id
        : entry.RegistryId == Identifier.Parse(ProviderRegistryId) ? entry.Value is IModuleBayProvider provider && entry.EntryId == provider.Id
        : entry.RegistryId == Identifier.Parse(CustomerRegistryId) ? entry.Value is CustomerDefinition && entry.EntryId.Namespace == owner
        : entry.RegistryId == Identifier.Parse(VolatileFeatureRegistryId) ? entry.Value is VolatileItemFeature && entry.EntryId.Namespace == owner
        : entry.RegistryId != Identifier.Parse(BindingRegistryId) || entry.Value is ModuleBayBinding;
}
