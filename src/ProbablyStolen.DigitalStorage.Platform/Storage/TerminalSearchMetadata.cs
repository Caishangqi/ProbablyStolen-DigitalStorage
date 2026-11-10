using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>Includes native type display names in detached type and corresponding enabled-tag terms.</summary>
internal static class TerminalSearchMetadata
{
    internal static SearchDocument WithTypeNames(SearchDocument source, IEnumerable<string> enabledTagIds,
        Func<string, string?> resolveTypeName)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (enabledTagIds == null) throw new ArgumentNullException(nameof(enabledTagIds));
        if (resolveTypeName == null) throw new ArgumentNullException(nameof(resolveTypeName));
        var tags = new List<string>(source.TagNames);
        var tagIds = new HashSet<string>(enabledTagIds, StringComparer.Ordinal);
        var types = new List<string>(source.ItemTypes);
        foreach (var type in source.ItemTypes.Distinct(StringComparer.Ordinal))
        {
            var label = resolveTypeName(type);
            if (string.IsNullOrWhiteSpace(label) || string.Equals(type, label, StringComparison.Ordinal)) continue;
            types.Add(label!);
            // SetGameItemType normally enables a same-ID tag, but never infer it if only itemTypes was saved.
            if (tagIds.Contains(type)) tags.Add(label!);
        }
        return new SearchDocument(source.Name, source.Identifier, tags.Distinct(StringComparer.Ordinal),
            types.Distinct(StringComparer.Ordinal), source.Descriptions,
            source.AddonFields.Select(pair => new KeyValuePair<string, IEnumerable<string>>(pair.Key, pair.Value)));
    }
}
