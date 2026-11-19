using System;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Api.Content;

/// <summary>Identity rules of the builtin customer registry.</summary>
public static class CustomerIdentity
{
    /// <summary>Gets the builtin customer registry identifier.</summary>
    public const string RegistryId = "digital_storage:customer";

    /// <summary>
    /// Gets the id the game's customer catalog uses for <paramref name="id"/>: <c>namespace.path</c>, matching how mod
    /// item ids are written. Saves store queued customers by this id.
    /// </summary>
    /// <exception cref="ArgumentException">The identifier is invalid.</exception>
    public static string ToNativeId(Identifier id)
    {
        if (!id.IsValid) throw new ArgumentException("A customer requires a valid identifier.", nameof(id));
        return id.Namespace + "." + id.Path;
    }

    /// <summary>
    /// Gets whether <paramref name="key"/> names a mod item sprite as <c>modId:spriteKey</c>, the form the game's
    /// <c>CustomUIManager.GetSprite</c> resolves.
    /// </summary>
    public static bool IsPortraitKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var separator = key!.IndexOf(':');
        return separator > 0 && separator == key.LastIndexOf(':') && separator < key.Length - 1
            && key.Trim() == key;
    }
}
