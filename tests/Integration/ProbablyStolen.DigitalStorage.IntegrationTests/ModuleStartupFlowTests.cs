using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Initialization;
using ProbablyStolen.DigitalStorage.Common.Initialization;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class ModuleStartupFlowTests
{
    [Test]
    public void StartupFlow_UsesValidatedCommonIdentityInClientDiagnostics()
    {
        var created = ModuleIdentity.TryCreate("digital_storage", "Digital Storage", out var identity, out var error);
        Assert.That(created, Is.True, error);

        var presenter = new StartupStatusPresenter(identity!);

        Assert.That(presenter.Present(ModuleStartupPhase.Initialized), Is.EqualTo("Digital Storage: initialized."));
        Assert.That(presenter.Present(ModuleStartupPhase.Enabled), Is.EqualTo("Digital Storage: enabled."));
        Assert.That(presenter.Present(ModuleStartupPhase.Disabled), Is.EqualTo("Digital Storage: disabled."));
    }

    [Test]
    public void StartupFlow_InvalidCommonIdentity_StopsBeforePresentation()
    {
        var created = ModuleIdentity.TryCreate("invalid id", "Digital Storage", out var identity, out var error);

        Assert.That(created, Is.False);
        Assert.That(identity, Is.Null);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }
}
