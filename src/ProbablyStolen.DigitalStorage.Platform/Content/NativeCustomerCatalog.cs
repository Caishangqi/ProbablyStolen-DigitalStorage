using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Puts the frozen customer registry into the game. The game has no customer registration API: its catalog
/// <c>StoreClientListDict.storeClientDict</c> and its client sprite dictionary <c>SpriteDict.spriteDictionary</c> are
/// public, every by-id customer creation resolves through the catalog, and a client's portrait is looked up in the
/// sprite dictionary. Only entries this catalog added are removed again.
/// </summary>
internal sealed class NativeCustomerCatalog : IDisposable
{
    private readonly ModLog _log;
    private readonly Dictionary<Identifier, Bound> _bound = new();
    private readonly Dictionary<string, Sprite> _portraits = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    internal NativeCustomerCatalog(ModLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// The customer registry's bake callback: adds every registered customer to the game's catalog. It runs once the
    /// registries freeze, before any save loads, so customer ids a save queued resolve again. An id the game or another
    /// mod already uses is left untouched.
    /// </summary>
    internal void Bind(IReadOnlyRegistry<CustomerDefinition> customers)
    {
        Unbind();
        foreach (var entry in customers.Entries)
        {
            var nativeId = CustomerIdentity.ToNativeId(entry.Key);
            if (StoreClientListDict.storeClientDict.ContainsKey(nativeId))
            {
                _log.Warning($"Digital Storage: customer id {nativeId} is already in the game's catalog; {entry.Key} is unavailable.");
                continue;
            }

            var definition = entry.Value;
            Func<StoreClient> factory = () => Create(definition);
            StoreClientListDict.storeClientDict.Add(nativeId, factory);
            _bound.Add(entry.Key, new Bound(nativeId, definition, factory));
        }

        _log.Log($"Digital Storage: {_bound.Count} customer(s) added to the game's catalog.");
    }

    /// <summary>Creates a bound customer for a visit the registering mod schedules; false when it cannot be created.</summary>
    internal bool TryCreate(Identifier id, out StoreClient client)
    {
        client = null!;
        if (!_bound.TryGetValue(id, out var bound)) return false;
        try
        {
            client = Create(bound.Definition);
            return true;
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: customer {id} was not created ({error.GetType().Name}: {error.Message}).");
            return false;
        }
    }

    public void Dispose()
    {
        Unbind();
        var sprites = ClientSprites();
        foreach (var portrait in _portraits)
        {
            if (sprites != null && sprites.TryGetValue(portrait.Key, out var present) && present == portrait.Value)
                sprites.Remove(portrait.Key);
        }

        _portraits.Clear();
    }

    private void Unbind()
    {
        foreach (var bound in _bound.Values)
        {
            if (StoreClientListDict.storeClientDict.TryGetValue(bound.NativeId, out var present) && present == bound.Factory)
                StoreClientListDict.storeClientDict.Remove(bound.NativeId);
        }

        _bound.Clear();
    }

    private StoreClient Create(CustomerDefinition definition)
    {
        // The portrait comes first: a customer without its declared portrait is not created.
        if (definition.Portrait != null) EnsurePortrait(definition.Portrait);
        var client = definition.Factory() ?? throw new InvalidOperationException("The customer factory returned null.");
        if (definition.Portrait != null) client.spriteName = definition.Portrait;
        // The game credits a catalog customer to the mod whose assembly holds the factory, which here would always be
        // this mod; crediting the registered factory keeps other mods' customers theirs.
        ModSourceHelper.MarkFromFactory(client, definition.Factory);
        return client;
    }

    /// <summary>
    /// Adds the portrait to the scene's client sprite dictionary. A new scene starts with a fresh dictionary, so this
    /// runs before every creation.
    /// </summary>
    private void EnsurePortrait(string key)
    {
        var sprites = ClientSprites() ?? throw new InvalidOperationException("The client sprite dictionary is not loaded.");
        if (!_portraits.TryGetValue(key, out var sprite) || sprite == null)
        {
            var separator = key.IndexOf(':');
            var modId = key.Substring(0, separator);
            var mod = ModLoader.Mods.Find(candidate => candidate.Manifest.ID == modId);
            if (mod == null || !mod.Sprites.TryGetValue(key.Substring(separator + 1), out sprite) || sprite == null)
            {
                if (_reported.Add(key)) _log.Error($"Digital Storage: customer portrait {key} is not loaded.");
                throw new InvalidOperationException("Customer portrait is not loaded: " + key);
            }

            _portraits[key] = sprite;
        }

        if (!sprites.TryGetValue(key, out var present) || present != sprite) sprites[key] = sprite;
    }

    // SpriteDict is a Unity object; a destroyed scene instance compares equal to null only through Unity's operator.
    private static Dictionary<string, Sprite>? ClientSprites() =>
        SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;

    private sealed class Bound
    {
        internal Bound(string nativeId, CustomerDefinition definition, Func<StoreClient> factory)
        {
            NativeId = nativeId;
            Definition = definition;
            Factory = factory;
        }

        internal string NativeId { get; }

        internal CustomerDefinition Definition { get; }
        internal Func<StoreClient> Factory { get; }
    }
}
