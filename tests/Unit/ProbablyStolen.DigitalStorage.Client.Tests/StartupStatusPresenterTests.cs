using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Initialization;
using ProbablyStolen.DigitalStorage.Common.Initialization;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class StartupStatusPresenterTests
{
    [TestCase(ModuleStartupPhase.Initialized, "Digital Storage: initialized.")]
    [TestCase(ModuleStartupPhase.Enabled, "Digital Storage: enabled.")]
    [TestCase(ModuleStartupPhase.Disabled, "Digital Storage: disabled.")]
    public void Present_KnownPhase_ReturnsStableDiagnostic(ModuleStartupPhase phase, string expected)
    {
        var presenter = CreatePresenter();

        var message = presenter.Present(phase);

        Assert.That(message, Is.EqualTo(expected));
    }

    [Test]
    public void Constructor_NullIdentity_Throws()
    {
        Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = new StartupStatusPresenter(null!);
        }));
    }

    [Test]
    public void Present_UnknownPhase_Throws()
    {
        var presenter = CreatePresenter();

        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
        {
            _ = presenter.Present((ModuleStartupPhase)99);
        }));
    }

    private static StartupStatusPresenter CreatePresenter()
    {
        Assert.That(ModuleIdentity.TryCreate("digital_storage", "Digital Storage", out var identity, out var error), Is.True, error);
        return new StartupStatusPresenter(identity!);
    }
}
