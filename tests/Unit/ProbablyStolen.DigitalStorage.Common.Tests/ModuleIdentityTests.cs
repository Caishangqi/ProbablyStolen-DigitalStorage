using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Initialization;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class ModuleIdentityTests
{
    [TestCase("digital_storage", "Digital Storage")]
    [TestCase("A-1.test_value", "Display Name")]
    public void TryCreate_ValidValues_PreservesInput(string id, string displayName)
    {
        var created = ModuleIdentity.TryCreate(id, displayName, out var identity, out var error);

        Assert.That(created, Is.True, error);
        Assert.That(identity, Is.Not.Null);
        Assert.That(identity!.Id, Is.EqualTo(id));
        Assert.That(identity.DisplayName, Is.EqualTo(displayName));
        Assert.That(error, Is.Null);
    }

    [TestCase(64, true)]
    [TestCase(65, false)]
    public void TryCreate_IdentifierLength_RespectsLoaderLimit(int length, bool expected)
    {
        var id = new string('a', length);

        var created = ModuleIdentity.TryCreate(id, "Name", out _, out _);

        Assert.That(created, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(".leading")]
    [TestCase("has space")]
    [TestCase("has/slash")]
    [TestCase("café")]
    public void TryCreate_InvalidIdentifier_ReturnsReason(string? id)
    {
        var created = ModuleIdentity.TryCreate(id, "Name", out var identity, out var error);

        Assert.That(created, Is.False);
        Assert.That(identity, Is.Null);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void TryCreate_BlankDisplayName_ReturnsReason(string? displayName)
    {
        var created = ModuleIdentity.TryCreate("digital_storage", displayName, out var identity, out var error);

        Assert.That(created, Is.False);
        Assert.That(identity, Is.Null);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Equals_UsesOrdinalIdentifier()
    {
        Assert.That(ModuleIdentity.TryCreate("Storage", "First", out var first, out _), Is.True);
        Assert.That(ModuleIdentity.TryCreate("Storage", "Second", out var second, out _), Is.True);
        Assert.That(ModuleIdentity.TryCreate("storage", "First", out var differentCase, out _), Is.True);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Is.Not.EqualTo(differentCase));
        Assert.That(first!.GetHashCode(), Is.EqualTo(second!.GetHashCode()));
    }
}
