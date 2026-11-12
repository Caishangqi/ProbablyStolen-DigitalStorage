using System;

namespace ProbablyStolen.DigitalStorage.Api.Storage;

/// <summary>Why a storage call stopped.</summary>
public enum StorageStatus
{
    /// <summary>Everything requested moved (or would move, when simulated).</summary>
    Done = 0,

    /// <summary>Some units moved, then the destination or the storage stopped accepting.</summary>
    Partial = 1,

    /// <summary>The storage holds none of the requested type.</summary>
    Empty = 2,

    /// <summary>The storage or the destination refused; nothing moved.</summary>
    Rejected = 3,

    /// <summary>The network is offline, a cell cannot be read, or a save is still decoding; nothing moved.</summary>
    Unavailable = 4,

    /// <summary>The call did not come from the main thread; nothing moved.</summary>
    WrongThread = 5,

    /// <summary>The root passed to <see cref="IStorage.ExtractRoot"/> is not stored in this network; nothing moved.</summary>
    NotStored = 6
}

/// <summary>The units a storage call moved and why it stopped.</summary>
public readonly struct StorageResult : IEquatable<StorageResult>
{
    /// <summary>Creates a result.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The units are negative.</exception>
    public StorageResult(long units, StorageStatus status)
    {
        Units = units >= 0 ? units : throw new ArgumentOutOfRangeException(nameof(units), units, "Moved units are not negative.");
        Status = status;
    }

    /// <summary>Gets the units that moved (or would move, when simulated).</summary>
    public long Units { get; }

    /// <summary>Gets why the call stopped.</summary>
    public StorageStatus Status { get; }

    /// <summary>Gets a result for a call that moved nothing.</summary>
    public static StorageResult None(StorageStatus status) => new(0, status);

    /// <inheritdoc />
    public bool Equals(StorageResult other) => Units == other.Units && Status == other.Status;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StorageResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Units, Status);

    /// <inheritdoc />
    public override string ToString() => $"{Status}({Units})";
}
