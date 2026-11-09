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

    /// <summary>Requires fourteen distinct slot ports and stores no physical items or mutable item ledger.</summary>
    public DriveBayBinding(DriveCellInventory rules, ICellBayPort[] bays)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        if (bays == null || bays.Length != rules.Size())
        {
            throw new ArgumentException("The drive requires fourteen independent bay ports.", nameof(bays));
        }

        for (var index = 0; index < bays.Length; index++)
        {
            if (bays[index] == null)
            {
                throw new ArgumentException("A bay port is missing.", nameof(bays));
            }

            for (var previous = 0; previous < index; previous++)
            {
                if (ReferenceEquals(bays[index], bays[previous]))
                {
                    throw new ArgumentException("Drive bays must not share a slot.", nameof(bays));
                }
            }
        }

        _bays = (ICellBayPort[])bays.Clone();
    }

    /// <summary>Uses live slot occupancy and complete cell facts before native placement.</summary>
    public bool CanInsert(int index, CellAssemblySnapshot candidate, bool isCurrent, bool ownershipAllowed)
    {
        if (index < 0 || index >= _bays.Length || !ownershipAllowed)
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
