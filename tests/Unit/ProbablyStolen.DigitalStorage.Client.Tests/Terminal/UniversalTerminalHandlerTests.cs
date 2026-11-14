using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using static ProbablyStolen.DigitalStorage.Client.Tests.Terminal.TerminalFixtures;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class UniversalTerminalHandlerTests
{
    private readonly List<string> _log = new();
    private MemoryStore _store = null!;
    private UniversalTerminalHandler _handler = null!;
    private bool _testAvailable;

    [SetUp]
    public void Build()
    {
        _log.Clear();
        _testAvailable = true;
        _store = new MemoryStore();
        var registry = new TerminalRegistry(new MemoryConfig(), _log.Add);
        registry.AddTerminal(Storage());
        registry.AddTerminal(View("test", 1, _ => _testAvailable));
        registry.AddTerminal(View("broken", 2, _ => throw new InvalidOperationException()));
        registry.Freeze();
        _handler = new UniversalTerminalHandler(registry, _store, _log.Add);
    }

    [TestCase("test", true, "test")]
    [TestCase("test", false, "storage")]
    [TestCase("unknown", true, "storage")]
    [TestCase(null, true, "storage")]
    [TestCase("", true, "storage")]
    [TestCase("broken", true, "storage")]
    public void GetCurrentTerminal_FallsBackToStorageAndNeverWrites(string? stored, bool available, string expected)
    {
        _store.Value = stored;
        _testAvailable = available;

        Assert.That(_handler.GetCurrentTerminal(Context()).TerminalName, Is.EqualTo(expected));
        Assert.That(_store.Written, Is.Empty);
        Assert.That(_store.Value, Is.EqualTo(stored), "The stored view is kept for later.");
    }

    [Test]
    public void GetCurrentTerminal_ReadFailureShowsStorage()
    {
        _store.FailRead = true;

        Assert.That(_handler.GetCurrentTerminal(Context()).TerminalName, Is.EqualTo("storage"));
        Assert.That(_log.Single(), Does.Contain("read=failed"));
    }

    [Test]
    public void SetCurrentTerminal_WritesOnlyTheNameAndSwallowsWriteFailures()
    {
        _handler.SetCurrentTerminal(View("test"));
        Assert.That(_store.Written, Is.EqualTo(new[] { "test" }));

        _store.FailWrite = true;
        Assert.DoesNotThrow(new Action(() => _handler.SetCurrentTerminal(View("storage"))));
        Assert.That(_log.Single(), Does.Contain("write=failed"));
    }

    [Test]
    public void InstalledTerminals_SkipUnavailableAndFailingViews()
    {
        Assert.That(_handler.InstalledTerminals(Context()).Select(terminal => terminal.TerminalName),
            Is.EqualTo(new[] { "storage", "test" }));
        _testAvailable = false;
        Assert.That(_handler.InstalledTerminals(Context()), Has.Count.EqualTo(1));
        Assert.That(_handler.HasTerminal(Context(), "test"), Is.False);
        Assert.That(_handler.HasTerminal(Context(), "missing"), Is.False);
        Assert.That(_log.Count(line => line.Contains("broken")), Is.EqualTo(1), "Logged once.");
    }
}
