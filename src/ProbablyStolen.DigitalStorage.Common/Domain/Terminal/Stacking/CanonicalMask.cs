using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>Removes owned root data from a canonical identity and encodes the rest again.</summary>
internal static class CanonicalMask
{
    /// <summary>Decodes a canonical identity, or returns null when the key has none or it cannot be read.</summary>
    internal static CanonicalIdentityInput? Decode(ItemKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        return key.IsCanonical ? CanonicalIdentityValidator.Decode(key.CopyCanonicalState(), key.Identifier) : null;
    }

    /// <summary>
    /// Returns the identity without the owned paths. The item identifier is never part of any path, so two masked
    /// identities can only match within one item definition.
    /// </summary>
    internal static CanonicalIdentityInput Strip(CanonicalIdentityInput input, StackingOwnedPaths paths)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (paths == null) throw new ArgumentNullException(nameof(paths));
        var tagKeys = new HashSet<string>(paths.TagKeys, StringComparer.Ordinal);
        var featureIds = new HashSet<string>(paths.FeatureIds, StringComparer.Ordinal);
        return new CanonicalIdentityInput(input.Identifier, input.ItemTypes,
            input.Tags.Where(tag => !tagKeys.Contains(tag.Key)).ToArray(),
            input.Features.Where(feature => !featureIds.Contains(feature.Identifier)).ToArray(),
            input.Name, input.ShortDescription, input.FlavorText,
            paths.ValueFields ? 0 : input.UnitBaseValue, paths.ValueFields ? 0 : input.UnitValue,
            paths.ValueFields ? 0 : input.LateUnitValue, paths.ValueFields ? 0 : input.BackupUnitValue,
            input.SpriteAtlasPath, input.SpritePath, paths.ContentWindow ? null : input.ContentWindow,
            input.ExamineWindow);
    }

    /// <summary>Encodes an identity, or returns null when it cannot be encoded.</summary>
    internal static byte[]? Encode(CanonicalIdentityInput input)
    {
        try
        {
            return CanonicalIdentityEncoder.Encode(input).CopyCanonicalState();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Encodes only the owned paths for an exact comparison between members of one group.</summary>
    internal static byte[]? EncodeOwned(CanonicalIdentityInput input, StackingOwnedPaths paths) =>
        Encode(new CanonicalIdentityInput(input.Identifier, Array.Empty<string>(),
            input.Tags.Where(tag => paths.TagKeys.Contains(tag.Key)).ToArray(),
            input.Features.Where(feature => paths.FeatureIds.Contains(feature.Identifier)).ToArray(),
            unitBaseValue: paths.ValueFields ? input.UnitBaseValue : 0,
            unitValue: paths.ValueFields ? input.UnitValue : 0,
            lateUnitValue: paths.ValueFields ? input.LateUnitValue : 0,
            backupUnitValue: paths.ValueFields ? input.BackupUnitValue : 0,
            contentWindow: paths.ContentWindow ? input.ContentWindow : null));

    /// <summary>Returns the encoded identity without the owned paths, or null when it cannot be encoded again.</summary>
    internal static byte[]? Apply(CanonicalIdentityInput input, StackingOwnedPaths paths) => Encode(Strip(input, paths));
}
