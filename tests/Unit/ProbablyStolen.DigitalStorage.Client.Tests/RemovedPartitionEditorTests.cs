using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Workbench;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

/// <summary>Partition editing lives only in the cell workbench; the terminal's editor types must not return.</summary>
[TestFixture]
public sealed class RemovedPartitionEditorTests
{
    [TestCase("PartitionEditor")]
    [TestCase("PartitionWindowController")]
    [TestCase("IPartitionWindowSource")]
    public void TerminalEditorTypeIsGone(string name)
    {
        var types = typeof(PartitionDraft).Assembly.GetTypes().Select(type => type.Name);

        Assert.That(types, Does.Not.Contain(name));
    }
}
