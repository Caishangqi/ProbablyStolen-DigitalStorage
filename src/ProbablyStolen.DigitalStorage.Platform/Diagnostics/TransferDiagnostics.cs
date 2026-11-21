using System.Collections.Generic;
using System.Threading;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
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
    private int _pairs;
    private bool _suppressed;

    internal TransferDiagnostics(DiagnosticLogger log, LogCategory category) => (_log, _category) = (log, category);

    internal TransferOperation Begin(string mode, long requestedPieces) =>
        new(Interlocked.Increment(ref _nextOperationId), mode, requestedPieces);

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
