using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Common.Diagnostics;

internal sealed class LogCategories
{
    private readonly Registry<LogCategory> _registry = new(new Identifier("digital_storage", "log_categories"));

    internal IReadOnlyList<LogCategory> Entries => Array.AsReadOnly(_registry.RegistrationEntries.Select(entry => entry.Value).ToArray());

    internal LogCategory Register(string id, string? labelKey, string fallbackLabel, LogVerbosity defaultVerbosity, bool defaultEnabled)
    {
        if (id == null || !id.StartsWith("log_", StringComparison.Ordinal) || !LogFormatter.IsToken(id))
            throw new ArgumentException("Category IDs use lowercase snake_case with the log_ prefix.", nameof(id));
        if (!DiagnosticLogger.IsLevel(defaultVerbosity)) throw new ArgumentOutOfRangeException(nameof(defaultVerbosity));
        var category = new LogCategory(id, labelKey, fallbackLabel, defaultVerbosity, defaultEnabled);
        _registry.Register(new Identifier("digital_storage", id), category);
        return category;
    }

    internal void Freeze() => _registry.Freeze();

    internal bool Contains(LogCategory category) =>
        _registry.TryGetValue(new Identifier("digital_storage", category.Id), out var registered) && ReferenceEquals(category, registered);
}
