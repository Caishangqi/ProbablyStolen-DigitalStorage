using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal;

[TestFixture]
public sealed class StorageScreenRevisionTests
{
    [Test]
    public void QuantityOnlyUpdateKeepsViewRevisionAndCarriesNewTotal()
    {
        var screen = new StorageScreen(2, 1);
        screen.SetEntriesInOrder(new[] { Entry("apple", 3, 1), Entry("banana", 2, 2) });
        var before = screen.GetPage();

        screen.UpdateEntriesInOrder(new[] { Entry("apple", 1, 1), Entry("banana", 2, 2) });
        var after = screen.GetPage();

        Assert.That(after.ViewRevision, Is.EqualTo(before.ViewRevision));
        Assert.That(after.Slots[0].Amount, Is.EqualTo(1));
    }

    [Test]
    public void QuantityUpdateAfterSearchConfigurationChangeInvalidatesViewEvenWhenGroupRemainsVisible()
    {
        var search = new TerminalSearchService(new SearchMatcherRegistry(), new SearchConfiguration());
        var screen = new StorageScreen(1, 1, search: search);
        screen.SetEntriesInOrder(new[] { Entry("apple", 3, 1) });
        var before = screen.GetPage();

        search.UpdateConfiguration(new SearchConfiguration(enabled: false));
        screen.UpdateEntriesInOrder(new[] { Entry("apple", 1, 1) });
        var after = screen.GetPage();

        Assert.That(after.Slots[0].Group!.Id, Is.EqualTo(before.Slots[0].Group!.Id));
        Assert.That(after.Slots[0].Amount, Is.EqualTo(1));
        Assert.That(after.ViewRevision, Is.GreaterThan(before.ViewRevision));
        Assert.That(screen.GetPage().ViewRevision, Is.EqualTo(after.ViewRevision));
    }

    [Test]
    public void MappingSearchScrollAndLayoutChangesAdvanceViewRevision()
    {
        var screen = new StorageScreen(1, 1);
        screen.SetEntriesInOrder(new[] { Entry("apple", 1, 1), Entry("banana", 1, 2) });
        var initial = screen.GetPage().ViewRevision;

        screen.SetSearchText("banana");
        Assert.That(screen.GetPage().ViewRevision, Is.GreaterThan(initial));
        var searched = screen.GetPage().ViewRevision;

        screen.SetSearchText(string.Empty);
        screen.SetScroll(1);
        Assert.That(screen.GetPage().ViewRevision, Is.GreaterThan(searched));
        var scrolled = screen.GetPage().ViewRevision;
        screen.SetRows(2);
        Assert.That(screen.GetPage().ViewRevision, Is.GreaterThan(scrolled));
    }

    [Test]
    public void PresentationRevisionIsCapturedWithoutChangingViewRevision()
    {
        var screen = new StorageScreen(1, 1);
        screen.SetEntriesInOrder(new[] { Entry("apple", 1, 1) });
        var before = screen.GetPage();

        var after = screen.GetPage(12);

        Assert.That(after.ViewRevision, Is.EqualTo(before.ViewRevision));
        Assert.That(after.PresentationRevision, Is.EqualTo(12));
        Assert.That(screen.GetPage().PresentationRevision, Is.EqualTo(12));
    }

    [Test]
    public void OrderedMemberOrRepresentativeMetadataChangeAdvancesViewRevision()
    {
        var screen = new StorageScreen(1, 1);
        screen.SetEntriesInOrder(new[]
        {
            new DisplayEntry("group", new[] { Entry("apple#one", 2, 1), Entry("apple#two", 2, 2) })
        });
        var before = screen.GetPage().ViewRevision;

        screen.UpdateEntriesInOrder(new[]
        {
            new DisplayEntry("group", new[] { Entry("apple#two", 2, 2), Entry("apple#one", 2, 1) })
        });

        Assert.That(screen.GetPage().ViewRevision, Is.GreaterThan(before));
    }

    private static TerminalEntry Entry(string id, long amount, long serial)
    {
        var split = id.Split('#');
        var key = split.Length == 1
            ? new ItemKey(id)
            : ItemKey.Of(split[0], "variant", split[1]);
        return new TerminalEntry(key, amount, split[0], "sprite", serial: serial);
    }
}
