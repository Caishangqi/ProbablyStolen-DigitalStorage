using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
internal sealed class TransferDiagnosticsTests
{
    [Test]
    public void Classify_SeparatesNativeIdChangePreciseKeyChangeAndUnreadableIdentity()
    {
        var expected = Precise(1);
        var fallback = new ItemKey("root");
        Assert.That(StoredRootIdentityMismatch.Classify(expected, null, "other", true), Is.EqualTo("item_id_changed"));
        Assert.That(StoredRootIdentityMismatch.Classify(expected, Precise(2), "root", false), Is.EqualTo("identity_key_changed"));
        Assert.That(StoredRootIdentityMismatch.Classify(expected, fallback, "root", true), Is.EqualTo("identity_unreadable"));
        Assert.That(StoredRootIdentityMismatch.Classify(fallback, fallback, "root", true), Is.Null);
        Assert.That(StoredRootIdentityMismatch.Classify(fallback, fallback, "other", true), Is.Null);
    }

    [Test]
    public void Extracted_UsesAggregateCountsForBothErrorsWithOneOperationId()
    {
        var logs = new List<string>();
        var diagnostics = Create(logs);
        var menu = new StorageMenu();
        menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[]
        {
            new TerminalEntry(Precise(1), 4, "Root", "root"),
            new TerminalEntry(Precise(2), 3, "Root", "root")
        }));
        menu.SetProjection(entries => new[] { new ProjectedGroup("root_group", entries) });
        menu.SetSort(TerminalSortOrder.Amount, TerminalSortDirection.Descending);
        var operation = diagnostics.Begin("all", 7);
        var calls = 0;
        var result = menu.HandleGroupInteraction(menu.GetEntries().Single(), 7, (_, pieces) =>
        {
            if (++calls == 1) return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, pieces, pieces, "done");
            operation.Record(new StoredRootIdentityMismatch("identity_key_changed", "root", "root", 6, 3, 1));
            return Failed(pieces, 1);
        });
        Assert.That(calls, Is.EqualTo(2));
        diagnostics.Extracted(operation, result);
        Assert.That(logs, Has.Count.EqualTo(2));
        Assert.That(logs[0], Does.Contain("event=stored_root_identity_mismatch").And.Contain("cell_uid=6 page=3 slot=1"));
        Assert.That(logs[1], Does.Contain("event=extract_result"));
        foreach (var log in logs)
            Assert.That(log, Does.Contain("level=error").And.Contain($"operation_id={operation.Id}").And.Contain("requested_pieces=7 moved_pieces=5"));
        Assert.That(diagnostics.Begin("drag", 1).Id, Is.GreaterThan(operation.Id));
    }

    [Test]
    public void Extracted_CapsRepeatedAndGlobalMismatchPairsWithOneNotice()
    {
        var logs = new List<string>();
        var diagnostics = Create(logs);
        for (var i = 0; i < 5; ++i) Mismatch(diagnostics, "same");
        for (var i = 0; i < 70; ++i) Mismatch(diagnostics, $"item_{i}");
        Assert.That(logs.Count(line => line.Contains("actual_item_id=")), Is.EqualTo(64));
        Assert.That(logs.Count(line => line.Contains("event=extract_result")), Is.EqualTo(64));
        Assert.That(logs.Count(line => line.Contains("event=stored_root_identity_mismatch_suppressed")), Is.EqualTo(1));
        Assert.That(logs.Count(line => line.Contains("actual_item_id=\"same\"")), Is.EqualTo(4));
    }

    private static TransferDiagnostics Create(List<string> logs)
    {
        var categories = new LogCategories();
        var category = categories.Register("log_transfer", null, "Transfer", LogVerbosity.Info, true);
        categories.Freeze();
        return new TransferDiagnostics(new DiagnosticLogger(categories,
            _ => new LogCategorySettings(true, LogVerbosity.Info), (_, line) => logs.Add(line)), category);
    }

    private static void Mismatch(TransferDiagnostics diagnostics, string actual)
    {
        var operation = diagnostics.Begin("drag", 3);
        operation.Record(new StoredRootIdentityMismatch("item_id_changed", "root", actual, 12, 1, 0));
        diagnostics.Extracted(operation, Failed(3, 1));
    }

    private static TerminalExtractionResult Failed(long requested, long moved) =>
        TerminalExtractionResult.Create(TerminalExtractionStatus.Failed, requested, moved, "terminal_storage_unavailable");

    private static ItemKey Precise(int value) => CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput("root",
        Array.Empty<string>(), new[] { new CanonicalIdentityTag("state", "state", "state", true, valueInt: value) },
        Array.Empty<CanonicalIdentityFeature>()));
}
