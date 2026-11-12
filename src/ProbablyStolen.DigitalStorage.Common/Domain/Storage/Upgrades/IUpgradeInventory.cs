using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

/// <summary>
/// Exposes installed modules without allowing changes. The module items themselves stay in the native module bay.
/// </summary>
public interface IUpgradeInventory
{
    /// <summary>Gets the installed modules in bay order.</summary>
    IReadOnlyList<InstalledModule> Modules { get; }

    /// <summary>Gets how many modules of a kind are installed, valid or not.</summary>
    int GetInstalledUpgrades(CellModuleKind kind);

    /// <summary>Gets whether at least one module of a kind is installed.</summary>
    bool IsInstalled(CellModuleKind kind) => GetInstalledUpgrades(kind) > 0;
}

/// <summary>An immutable <see cref="IUpgradeInventory"/>.</summary>
public sealed class UpgradeInventory : IUpgradeInventory
{
    private readonly IReadOnlyList<InstalledModule> _modules;

    /// <summary>Creates a view over modules in bay order.</summary>
    public UpgradeInventory(IEnumerable<InstalledModule> modules)
    {
        if (modules == null) throw new ArgumentNullException(nameof(modules));
        var list = modules.ToList();
        if (list.Any(module => module == null)) throw new ArgumentException("A module cannot be null.", nameof(modules));
        _modules = list.AsReadOnly();
    }

    /// <summary>Gets a view without modules.</summary>
    public static UpgradeInventory Empty { get; } = new(Array.Empty<InstalledModule>());

    /// <inheritdoc />
    public IReadOnlyList<InstalledModule> Modules => _modules;

    /// <inheritdoc />
    public int GetInstalledUpgrades(CellModuleKind kind) => _modules.Count(module => module.Kind == kind);

    /// <summary>Gets the view after one more module is installed.</summary>
    public UpgradeInventory With(InstalledModule module) =>
        new(_modules.Append(module ?? throw new ArgumentNullException(nameof(module))));

    /// <summary>Gets the view after the module at a bay index is removed.</summary>
    public UpgradeInventory Without(int index)
    {
        if (index < 0 || index >= _modules.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "No module at this index.");
        return new UpgradeInventory(_modules.Where((_, position) => position != index));
    }
}
