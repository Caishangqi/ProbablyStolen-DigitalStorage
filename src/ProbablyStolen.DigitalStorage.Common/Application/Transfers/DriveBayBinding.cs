using System;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>Binds the fixed bay rules to the independently owned native slots of one drive.</summary>
public sealed class DriveBayBinding
{
    private readonly DriveCellInventory _rules;
    private readonly ICellBayPort[] _bays;

    /// <summary>Requires one slot port per bay, in bay order, and stores no physical items or mutable item ledger.</summary>
    public DriveBayBinding(DriveCellInventory rules, ICellBayPort[] bays)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        if (bays == null || bays.Length != rules.Size())
        {
            throw new ArgumentException("The drive requires one bay port per bay.", nameof(bays));
        }

        _bays = (ICellBayPort[])bays.Clone();
    }

    /// <summary>
    /// Uses live slot occupancy and complete cell facts before native placement. <paramref name="index"/> must be a
    /// bay of this drive (0 to <see cref="DriveCellInventory.Size"/> - 1).
    /// </summary>
    public bool CanInsert(int index, CellAssemblySnapshot candidate, bool isCurrent, bool ownershipAllowed)
    {
        if (!ownershipAllowed)
        {
            return false;
        }

        if (_bays[index].Read() != null && !isCurrent)
        {
            return false;
        }

        return _rules.IsItemValid(index, candidate.Host.Identifier, candidate.Host.Count,
            candidate.Host.Width, candidate.Host.Height,
            candidate.Component?.Identifier, candidate.Component?.Count ?? 0);
    }
}
