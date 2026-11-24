using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal sealed class TransferDiagnostics
{
    private const int MaximumPairs = 64;
    private const int MaximumPairsPerIdentity = 4;
    private static long _nextOperationId;
    private readonly DiagnosticLogger _log;
    private readonly LogCategory _category;
    private readonly Dictionary<(string Reason, string Expected, string Actual), int> _mismatches = new();
    private readonly Dictionary<(int Slot, IoPortTransferFault Fault), int> _ioPortFaults = new();
    private int _pairs;
    private const int IoPortBatchLogSeconds = 5;
    private long _lastIoPortBatch = long.MinValue / 2;
    private int _ioPortWarnings;
    private bool _suppressed;

    internal TransferDiagnostics(DiagnosticLogger log, LogCategory category) => (_log, _category) = (log, category);

    internal TransferOperation Begin(string mode, long requestedPieces) =>
        new(Interlocked.Increment(ref _nextOperationId), mode, requestedPieces);

    /// <summary>
    /// Logs where one IO Port session spent its time, at most once every few seconds: reading the network once, moving
    /// roots over its steps, keeping the touched cells' contents between steps and at the end, and compacting.
    /// </summary>
    internal void IoPortBatch(int moved, int limit, int steps, long mountTicks, long moveTicks, long keepTicks, long compactTicks)
    {
        if (moved == 0 || !_log.IsEnabled(_category, LogVerbosity.Info)) return;
        var now = Stopwatch.GetTimestamp();
        if (now - _lastIoPortBatch < Stopwatch.Frequency * IoPortBatchLogSeconds) return;
        _lastIoPortBatch = now;
        _log.Write(_category, LogVerbosity.Info, "io_port_batch", "IO Port batch timing.", () => new[]
        {
            LogField.Of("moved", moved), LogField.Of("limit", limit), LogField.Of("steps", steps),
            LogField.Of("mount_ms", Milliseconds(mountTicks)), LogField.Of("move_ms", Milliseconds(moveTicks)),
            LogField.Of("per_root_ms", Milliseconds(moveTicks / moved)), LogField.Of("keep_ms", Milliseconds(keepTicks)),
            LogField.Of("compact_ms", Milliseconds(compactTicks)),
            Part("identify", TransferPart.Identify), Part("native_move", TransferPart.NativeMove)
        });
        // The formatter keeps 16 fields per line, so the parts of a root's move get a line of their own. Parts nest:
        // source_extract holds the whole move from a cell, network_insert and cell_insert the store into the target.
        _log.Write(_category, LogVerbosity.Info, "io_port_batch_parts", "IO Port batch parts.", () => new[]
        {
            Calls("identifies", TransferPart.Identify), Part("placement_check", TransferPart.PlacementCheck),
            Part("layout_read", TransferPart.LayoutRead), Part("source_peek", TransferPart.SourcePeek),
            Calls("source_peeks", TransferPart.SourcePeek), Part("store_plan", TransferPart.StorePlan),
            Calls("store_plans", TransferPart.StorePlan), Part("partition_check", TransferPart.PartitionCheck),
            Calls("partition_checks", TransferPart.PartitionCheck), Part("view_record", TransferPart.ViewRecord),
            Part("network_insert", TransferPart.NetworkInsert), Calls("network_inserts", TransferPart.NetworkInsert),
            Part("cell_insert", TransferPart.CellInsert), Part("source_extract", TransferPart.SourceExtract)
        });
    }

    private static LogField Part(string name, TransferPart part) => LogField.Of(name + "_ms", Milliseconds(TransferProfile.TicksOf(part)));

    private static LogField Calls(string name, TransferPart part) => LogField.Of(name, TransferProfile.CallsOf(part));

    private static string Milliseconds(long ticks) =>
        (ticks * 1000d / Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture);

    internal void IoPortFault(int slot, IoPortTransferFault fault)
    {
        if (_ioPortWarnings >= MaximumPairs || !_log.IsEnabled(_category, LogVerbosity.Warning)) return;
        var key = (slot, fault);
        _ioPortFaults.TryGetValue(key, out var count);
        if (count >= MaximumPairsPerIdentity) return;
        _ioPortWarnings++;
        _ioPortFaults[key] = count + 1;
        _log.Write(_category, LogVerbosity.Warning, "io_port_transfer_fault", "IO Port slot attempt failed.",
            () => new[] { LogField.Of("slot", slot), LogField.Of("fault", fault.ToString()) });
    }

    internal void Extracted(TransferOperation operation, TerminalExtractionResult result)
    {
        var outcome = result.Status switch
        {
            TerminalExtractionStatus.NoItems => "no_items",
            TerminalExtractionStatus.InvalidAmount => "invalid_amount",
            TerminalExtractionStatus.TargetRejected => "target_rejected",
            _ => result.Status.ToString().ToLowerInvariant()
        };
        if (operation.Mismatch is { } mismatch)
        {
            if (!_log.IsEnabled(_category, LogVerbosity.Error) || !TakePair(operation, mismatch)) return;
            _log.Write(_category, LogVerbosity.Error, "stored_root_identity_mismatch", "Stored root identity no longer matches its slot.",
                () => new[]
                {
                    LogField.Of("expected_item_id", mismatch.ExpectedId), LogField.Of("actual_item_id", mismatch.ActualId),
                    LogField.Of("cell_uid", mismatch.CellUid), LogField.Of("page", mismatch.Page), LogField.Of("slot", mismatch.Slot),
                    LogField.Of("reason", mismatch.Reason), LogField.Of("mode", operation.Mode),
                    LogField.Of("requested_pieces", result.RequestedAmount), LogField.Of("moved_pieces", result.ExtractedAmount),
                    LogField.Of("outcome", outcome)
                }, operation.Id);
        }

        var level = operation.Mismatch != null || operation.ExceptionType != null || operation.Unowned ||
            result.Status == TerminalExtractionStatus.Failed ? LogVerbosity.Error :
            result.Status == TerminalExtractionStatus.Partial ? LogVerbosity.Info : LogVerbosity.Verbose;
        _log.Write(_category, level, "extract_result", "Terminal extraction completed.", () => new[]
        {
            LogField.Of("mode", operation.Mode), LogField.Of("requested_pieces", result.RequestedAmount),
            LogField.Of("moved_pieces", result.ExtractedAmount), LogField.Of("outcome", outcome)
        }, operation.Id, operation.Mismatch == null ? operation.ExceptionType : null);
    }

    internal void Stored(TransferOperation operation, TerminalInsertionResult result)
    {
        var partial = result.MessageKey == "terminal_storage_inserted_partial";
        var level = operation.ExceptionType != null || operation.Unowned || result.Status == TerminalInsertionStatus.Failed ? LogVerbosity.Error :
            partial ? LogVerbosity.Info : LogVerbosity.Verbose;
        var outcome = partial ? "partial" : result.Status == TerminalInsertionStatus.NoItems ? "no_items" :
            result.Status.ToString().ToLowerInvariant();
        _log.Write(_category, level, "store_result", "Terminal store completed.", () => new[]
        {
            LogField.Of("mode", operation.Mode), LogField.Of("requested_pieces", operation.RequestedPieces),
            LogField.Of("moved_units", result.StoredAmount), LogField.Of("outcome", outcome),
            LogField.Of("transfer_elapsed_ms", operation.ElapsedMilliseconds)
        }, operation.Id, operation.ExceptionType);
    }

    internal void Failed(TransferOperation operation, bool extraction) =>
        _log.Write(_category, LogVerbosity.Error, extraction ? "extract_result" : "store_result",
            "Terminal transfer failed before a result was available.", () => new[]
            {
                LogField.Of("mode", operation.Mode), LogField.Of("requested_pieces", operation.RequestedPieces),
                LogField.Of("transfer_elapsed_ms", operation.ElapsedMilliseconds), LogField.Of("outcome", "failed")
            }, operation.Id, operation.ExceptionType);

    private bool TakePair(TransferOperation operation, StoredRootIdentityMismatch mismatch)
    {
        var key = (mismatch.Reason, mismatch.ExpectedId, mismatch.ActualId);
        _mismatches.TryGetValue(key, out var count);
        if (_pairs < MaximumPairs && count < MaximumPairsPerIdentity)
        {
            ++_pairs;
            _mismatches[key] = count + 1;
            return true;
        }
        if (!_suppressed)
        {
            _suppressed = true;
            _log.Write(_category, LogVerbosity.Error, "stored_root_identity_mismatch_suppressed",
                "Further stored root identity mismatches are limited.", operationId: operation.Id);
        }
        return false;
    }
}
