using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class DropRefusalReasonsTests
{
    [TestCase("terminal_storage_reject_not_owned", "terminal_drag_reason_not_owned")]
    [TestCase("terminal_storage_whole_stack_only", "terminal_drag_reason_whole_stack")]
    [TestCase("terminal_storage_reject_no_space", "terminal_drag_reason_full")]
    [TestCase("storage_restore_unavailable", "terminal_drag_reason_store_disabled")]
    public void ReasonKey_GivesTheShortFormOfAKnownRejection(string rejection, string expected)
    {
        Assert.That(DropRefusalReasons.ReasonKey(rejection), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("terminal_storage_some_future_rejection")]
    public void ReasonKey_FallsBackForMissingOrUnknownRejections(string? rejection)
    {
        Assert.That(DropRefusalReasons.ReasonKey(rejection), Is.EqualTo(DropRefusalReasons.FallbackKey));
    }

    [Test]
    public void AllReasonKeys_IncludesTheFallbackOnce()
    {
        Assert.That(DropRefusalReasons.AllReasonKeys, Does.Contain(DropRefusalReasons.FallbackKey));
        Assert.That(DropRefusalReasons.AllReasonKeys, Is.Unique);
    }
}
