using System;

namespace ProbablyStolen.DigitalStorage.Api.Content;

/// <summary>
/// A store customer in the builtin <see cref="DigitalStorageRegistries.Customer"/> registry. It carries no identifier:
/// its registry entry id names it, and the game's customer catalog lists it as <c>namespace.path</c>
/// (<see cref="CustomerIdentity.ToNativeId"/>). When the registries freeze, the registry adds every
/// customer to that catalog, so the game can create it by id (queued future customers, the debug customer viewer);
/// contributors never touch the catalog. A definition says who the customer is; when it visits stays with the
/// registering mod. Each story beat is its own entry, as in the game, and beats of one character share a portrait.
/// </summary>
public sealed class CustomerDefinition
{
    /// <summary>Creates a customer definition.</summary>
    /// <param name="factory">Creates a new, fully set up customer on every call.</param>
    /// <param name="portrait">
    /// An optional mod item sprite, <c>modId:spriteKey</c>. Before each creation it is added to the game's client sprite
    /// dictionary under this key and the customer's <c>spriteName</c> is set to it. A missing sprite is an error; no
    /// other portrait is substituted.
    /// </param>
    public CustomerDefinition(Func<StoreClient> factory, string? portrait = null)
    {
        if (portrait != null && !CustomerIdentity.IsPortraitKey(portrait))
            throw new ArgumentException("A portrait uses modId:spriteKey syntax.", nameof(portrait));
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Portrait = portrait;
    }

    /// <summary>Gets the customer factory.</summary>
    public Func<StoreClient> Factory { get; }

    /// <summary>Gets the optional portrait sprite key.</summary>
    public string? Portrait { get; }
}
