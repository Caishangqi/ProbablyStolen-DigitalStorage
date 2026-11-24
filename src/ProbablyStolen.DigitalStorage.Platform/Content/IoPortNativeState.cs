using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// The IO Port settings and per-slot fee counters, kept in two private tags on the machine. Cells and modules stay in
/// the native item graph; nothing here holds item data.
/// </summary>
internal sealed class IoPortNativeState
{
    internal const string SchemaTag = "digital_storage.io_port.schema";
    internal const string DataTag = "digital_storage.io_port.data";
    private const int SchemaVersion = 2;
    private const int FirstSchema = 1;
    private const int FirstSchemaNetworkMode = 1;
    private const int MaximumDataLength = 512;
    private const int SlotCount = IoPortSettings.SlotsPerBank;

    private readonly (int CellId, long Count)?[] _jobs;

    private IoPortNativeState(IoPortSettings settings, (int, long)?[] jobs)
    {
        Settings = settings;
        _jobs = jobs;
    }

    internal static IoPortNativeState Default { get; } = new(IoPortSettings.Default, new (int, long)?[SlotCount]);
    internal IoPortSettings Settings { get; }

    /// <summary>Keeps the fee counters unless the direction changed, which starts new jobs.</summary>
    internal IoPortNativeState WithSettings(IoPortSettings settings) =>
        new(settings, Settings.Direction == settings.Direction ? _jobs : new (int, long)?[SlotCount]);

    /// <summary>Drops every fee counter, as cancelling does.</summary>
    internal IoPortNativeState WithoutJobs() => new(Settings, new (int, long)?[SlotCount]);

    /// <summary>Gets the verified roots of the job bound to this cell, or zero for a new job.</summary>
    internal long CommittedRoots(int slot, int cellId)
    {
        ValidateSlot(slot);
        return _jobs[slot] is { } job && job.CellId == cellId ? job.Count : 0;
    }

    internal IoPortNativeState WithJob(int slot, int cellId, long committedRoots)
    {
        ValidateSlot(slot);
        if (cellId <= 0) throw new ArgumentOutOfRangeException(nameof(cellId));
        if (committedRoots < 0) throw new ArgumentOutOfRangeException(nameof(committedRoots));
        if (_jobs[slot] is { } job && job.CellId == cellId && job.Count == committedRoots) return this;
        var jobs = ((int, long)?[])_jobs.Clone();
        jobs[slot] = committedRoots == 0 ? null : (cellId, committedRoots);
        return new IoPortNativeState(Settings, jobs);
    }

    internal IoPortNativeState WithoutJob(int slot)
    {
        ValidateSlot(slot);
        if (!_jobs[slot].HasValue) return this;
        var jobs = ((int, long)?[])_jobs.Clone();
        jobs[slot] = null;
        return new IoPortNativeState(Settings, jobs);
    }

    internal static bool TryRead(GameItem machine, [NotNullWhen(true)] out IoPortNativeState? state)
    {
        state = null;
        if (machine.identifier != IoPortItem.ItemId || machine.state?.dict is not { } tags) return false;
        // Read the saved state directly. GetTag creates missing entries in the derived tag dictionary.
        var hasSchema = tags.TryGetValue(SchemaTag, out var schema);
        var hasData = tags.TryGetValue(DataTag, out var data);
        if (hasSchema && (schema == null || !schema.IsEnabled())) return false;
        if (hasData && (data == null || !data.IsEnabled() || !hasSchema)) return false;
        return TryDecode(hasSchema ? schema!.GetInt() : null, hasData ? data!.GetString() : null, out state);
    }

    /// <summary>Writes <paramref name="replacement"/> when the saved state still equals <paramref name="expected"/>.</summary>
    internal static bool TryWrite(GameItem machine, IoPortNativeState expected, IoPortNativeState replacement)
    {
        if (SaveManager.IsDecoding || !TryRead(machine, out var current)) return false;
        var previous = current.Encode();
        if (previous != expected.Encode()) return false;
        var next = replacement.Encode();
        if (previous == next && machine.state.dict.ContainsKey(SchemaTag) &&
            machine.state.dict[SchemaTag].GetInt() == SchemaVersion) return true;

        // These tags are private bookkeeping. Shape and module effects do not depend on them.
        machine.ModifyTag(SchemaTag, tag => tag.Enable().SetInt(SchemaVersion), skipValidate: true);
        machine.ModifyTag(DataTag, tag => tag.Enable().SetString(next), skipValidate: true);
        return true;
    }

    // The header holds direction, completion and the running flag. Six slot records follow in bank order; each
    // occupied record holds the input cell's unique ID and the verified root count of its job.
    internal string Encode()
    {
        var fields = new string[SlotCount + 1];
        fields[0] = FormattableString.Invariant(
            $"{(int)Settings.Direction},{(int)Settings.CompletionMode},{(Settings.Running ? 1 : 0)}");
        for (var slot = 0; slot < SlotCount; slot++)
            fields[slot + 1] = _jobs[slot] is { } job ? FormattableString.Invariant($"{job.CellId},{job.Count}") : string.Empty;
        return string.Join("|", fields);
    }

    internal static bool TryDecode(int? schema, string? data, [NotNullWhen(true)] out IoPortNativeState? state)
    {
        state = null;
        if (data == null)
        {
            if (schema is null or FirstSchema or SchemaVersion) state = Default;
            return state != null;
        }
        if (data.Length > MaximumDataLength) return false;
        return schema switch
        {
            SchemaVersion => TryDecodeCurrent(data, out state),
            FirstSchema => TryDecodeFirst(data, out state),
            _ => false
        };
    }

    private static bool TryDecodeCurrent(string data, [NotNullWhen(true)] out IoPortNativeState? state)
    {
        state = null;
        var fields = data.Split('|');
        if (fields.Length != SlotCount + 1) return false;
        var header = fields[0].Split(',');
        if (header.Length != 3 || !ParseInt(header[0], out var direction) || !ParseInt(header[1], out var completion) ||
            !ParseInt(header[2], out var running) || running > 1 || !TryCreateSettings(direction, completion, running == 1, out var settings))
            return false;

        var jobs = new (int, long)?[SlotCount];
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (fields[slot + 1].Length == 0) continue;
            var job = fields[slot + 1].Split(',');
            if (job.Length != 2 || !ParseInt(job[0], out var cellId) || cellId <= 0 || !ParseLong(job[1], out var count))
                return false;
            jobs[slot] = (cellId, count);
        }

        var decoded = new IoPortNativeState(settings, jobs);
        // Numeric parsing can accept trailing NULs. Only accept the format this schema writes.
        if (!string.Equals(decoded.Encode(), data, StringComparison.Ordinal)) return false;
        state = decoded;
        return true;
    }

    // Schema 1 records a mode and a right-bank ID per job. Its cell-and-network records convert, with the right-bank
    // ID dropped; a machine whose record holds the other mode reads as unavailable and keeps its tags, so the player can
    // still take its cells out. A converted machine starts paused, like a new one, and waits for Start.
    private static bool TryDecodeFirst(string data, [NotNullWhen(true)] out IoPortNativeState? state)
    {
        state = null;
        var fields = data.Split('|');
        if (fields.Length != SlotCount + 1) return false;
        var header = fields[0].Split(',');
        if (header.Length != 3 || !ParseInt(header[0], out var mode) || mode != FirstSchemaNetworkMode ||
            !ParseInt(header[1], out var direction) || !ParseInt(header[2], out var completion) ||
            !TryCreateSettings(direction, completion, false, out var settings))
            return false;

        var jobs = new (int, long)?[SlotCount];
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (fields[slot + 1].Length == 0) continue;
            var job = fields[slot + 1].Split(',');
            if (job.Length != 3 || !ParseInt(job[0], out var cellId) || cellId <= 0 || job[1] != "0" ||
                !ParseLong(job[2], out var count))
                return false;
            jobs[slot] = (cellId, count);
        }
        state = new IoPortNativeState(settings, jobs);
        return true;
    }

    private static bool TryCreateSettings(int direction, int completion, bool running, [NotNullWhen(true)] out IoPortSettings? settings)
    {
        settings = Enum.IsDefined(typeof(IoPortDirection), direction) && Enum.IsDefined(typeof(IoPortCompletionMode), completion)
            ? new IoPortSettings((IoPortDirection)direction, (IoPortCompletionMode)completion, running) : null;
        return settings != null;
    }

    private static bool ParseInt(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);

    private static bool ParseLong(string value, out long parsed) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);

    private static void ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
    }
}
