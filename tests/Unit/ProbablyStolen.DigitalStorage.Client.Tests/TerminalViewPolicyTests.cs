using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class TerminalViewPolicyTests
{
    [Test]
    public void Normalize_KeepsDefinedChoices()
    {
        Assert.That(TerminalViewPolicy.Normalize(TerminalSortOrder.Amount), Is.EqualTo(TerminalSortOrder.Amount));
        Assert.That(TerminalViewPolicy.Normalize(TerminalSortDirection.Descending), Is.EqualTo(TerminalSortDirection.Descending));
        Assert.That(TerminalViewPolicy.Normalize(TerminalSize.Full), Is.EqualTo(TerminalSize.Full));
    }

    [Test]
    public void Normalize_ReadsAnUndefinedStoredValueAsTheDefault()
    {
        // The settings file is player-editable and a numeric string parses to any enum value.
        Assert.That(TerminalViewPolicy.Normalize((TerminalSortOrder)7), Is.EqualTo(TerminalSortOrder.Name));
        Assert.That(TerminalViewPolicy.Normalize((TerminalSortDirection)(-1)), Is.EqualTo(TerminalSortDirection.Ascending));
        Assert.That(TerminalViewPolicy.Normalize((TerminalSize)0), Is.EqualTo(TerminalSize.Small));
        Assert.That(TerminalViewPolicy.Normalize((TerminalSize)5), Is.EqualTo(TerminalSize.Small));
        Assert.That(TerminalViewPolicy.Normalize((TerminalSize)9, TerminalSize.Medium), Is.EqualTo(TerminalSize.Medium));
        Assert.That(TerminalViewPolicy.Normalize(TerminalSize.Tall, TerminalSize.Medium), Is.EqualTo(TerminalSize.Tall));
    }
}
