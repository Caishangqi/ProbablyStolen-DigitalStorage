using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Identity;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
internal sealed class StoredItemChangesTests
{
    private readonly Dictionary<GameItem, (GameItem Cell, GameItem Root)> _stored = new();
    private readonly List<Action> _scheduled = new();
    private readonly List<(GameItem Cell, GameItem Root)> _checked = new();
    private readonly List<GameItem> _forgotten = new();
    private Func<GameItem, GameItem, bool> _stillMatches = (_, _) => true;
    private int _changed;
    private GameItem _cell = null!;
    private GameItem _root = null!;

    [SetUp]
    public void SetUp()
    {
        _stored.Clear();
        _scheduled.Clear();
        _checked.Clear();
        _forgotten.Clear();
        _stillMatches = (_, _) => true;
        _changed = 0;
        _cell = new GameItem("cell");
        _root = Stored("bread");
        var checks = new StoredRootChecks<GameItem, GameItem>(_scheduled.Add, (cell, root) =>
        {
            _checked.Add((cell, root));
            return _stillMatches(cell, root);
        }, _forgotten.Add, () => _changed++);
        StoredItemChanges.Activate(new StoredItemChanges(
            item => _stored.TryGetValue(item, out var located) ? located : null, () => false, checks));
    }

    [TearDown]
    public void TearDown()
    {
        StoredItemChanges.Activate(null);
        VolatileFeatureRules.Reset();
    }

    [Test]
    public void Read_UndoesFeatureAndLoreChangesOfTheRoot()
    {
        var kept = Feature(_root, "quality", owner: 7);
        _root.shortDescription = "Short";

        using (StoredItemChanges.Read(_root))
        {
            Change(_root, item => item.itemFeatures.Add(new ItemFeature("price_line") { parentItemUniqueId = 7 }));
            Change(_root, item => item.itemFeatures.Remove(kept));
            Change(_root, item => item.shortDescription = "Short\nwrapped");
        }

        Assert.That(_root.itemFeatures, Is.EqualTo(new[] { kept }));
        Assert.That(kept.parentItemUniqueId, Is.EqualTo(7));
        Assert.That(_root.shortDescription, Is.EqualTo("Short"));
        Assert.That(_scheduled, Is.Empty);
    }

    [Test]
    public void Read_UndoesRootChangesNoPatchReported()
    {
        _root.flavorText = "Flavor";

        using (StoredItemChanges.Read(_root))
        {
            _root.flavorText = "Flavor\nwrapped";
            _root.itemFeatures.Add(new ItemFeature("unreported"));
        }

        Assert.That(_root.flavorText, Is.EqualTo("Flavor"));
        Assert.That(_root.itemFeatures, Is.Empty);
    }

    [Test]
    public void Read_LeavesVolatileFeaturesAsTheReadLeftThem()
    {
        VolatileFeatureRules.Publish(new VolatileFeatureRules(new[] { "prof.sell." }));
        var kept = Feature(_root, "quality");
        var oldLine = Feature(_root, "prof.sell.0");
        var added = new ItemFeature("added") { parentItemUniqueId = 3 };
        var newLine = new ItemFeature("prof.sell.1");

        using (StoredItemChanges.Read(_root))
        {
            Change(_root, item => item.itemFeatures.Remove(oldLine));
            Change(_root, item => item.itemFeatures.Add(newLine));
            Change(_root, item => item.itemFeatures.Add(added));
        }

        Assert.That(_root.itemFeatures, Is.EqualTo(new[] { kept, newLine }));
        Assert.That(added.parentItemUniqueId, Is.EqualTo(-1));
    }

    [Test]
    public void Read_OnlyVolatileChanges_LeaveTheListAlone()
    {
        VolatileFeatureRules.Publish(new VolatileFeatureRules(new[] { "prof.sell." }));
        var kept = Feature(_root, "quality");
        var line = new ItemFeature("prof.sell.0");

        using (StoredItemChanges.Read(_root)) Change(_root, item => item.itemFeatures.Insert(0, line));

        Assert.That(_root.itemFeatures, Is.EqualTo(new[] { line, kept }));
    }

    [Test]
    public void Read_UndoesChangesToItemsInsideTheRoot()
    {
        var child = new GameItem("crumb") { shortDescription = "Crumb" };
        _stored[child] = (_cell, _root);

        using (StoredItemChanges.Read(_root)) Change(child, item => item.shortDescription = "Crumb\nwrapped");

        Assert.That(child.shortDescription, Is.EqualTo("Crumb"));
    }

    [Test]
    public void Read_TagChange_ChecksTheRootAfterTheRead()
    {
        using (StoredItemChanges.Read(_root))
        {
            StoredItemChanges.Changed(_root, restorable: false);
            Assert.That(_scheduled, Is.Empty);
        }

        RunScheduled();
        Assert.That(_checked, Is.EqualTo(new[] { (_cell, _root) }));
    }

    [Test]
    public void Read_OfTheSameRootInsideARead_LeavesRestoringToTheOuterRead()
    {
        _root.shortDescription = "Short";

        using (StoredItemChanges.Read(_root))
        {
            using (StoredItemChanges.Read(_root)) Change(_root, item => item.shortDescription = "Inner");
            Assert.That(_root.shortDescription, Is.EqualTo("Inner"));
        }

        Assert.That(_root.shortDescription, Is.EqualTo("Short"));
    }

    [Test]
    public void Read_OfAnItemNotStored_ChangesNothing()
    {
        var loose = new GameItem("loose") { shortDescription = "Loose" };

        using (StoredItemChanges.Read(loose)) Change(loose, item => item.shortDescription = "Changed");

        Assert.That(loose.shortDescription, Is.EqualTo("Changed"));
        Assert.That(_scheduled, Is.Empty);
    }

    [Test]
    public void Changed_OutsideARead_ChecksEachCellOnceAndRereadsOnlyAMismatch()
    {
        var other = Stored("cheese");
        _stillMatches = (_, root) => !ReferenceEquals(root, other);

        StoredItemChanges.Changed(_root, restorable: true);
        StoredItemChanges.Changed(other, restorable: true);
        StoredItemChanges.Changed(_root, restorable: false);

        Assert.That(_scheduled, Has.Count.EqualTo(1));
        RunScheduled();
        Assert.That(_forgotten, Is.EqualTo(new[] { _cell }));
        Assert.That(_changed, Is.EqualTo(1));
    }

    [Test]
    public void Changed_ForAnItemNotStored_IsIgnored()
    {
        StoredItemChanges.Changed(new GameItem("loose"), restorable: true);

        Assert.That(_scheduled, Is.Empty);
    }

    [Test]
    public void Silence_IgnoresChangesUntilDisposed()
    {
        using (StoredItemChanges.Silence()) StoredItemChanges.Changed(_root, restorable: false);
        Assert.That(_scheduled, Is.Empty);

        StoredItemChanges.Changed(_root, restorable: false);
        Assert.That(_scheduled, Has.Count.EqualTo(1));
    }

    [Test]
    public void Deactivated_IgnoresChangesAndReads()
    {
        StoredItemChanges.Activate(null);
        _root.shortDescription = "Short";

        using (StoredItemChanges.Read(_root)) _root.shortDescription = "Changed";
        StoredItemChanges.Changed(_root, restorable: true);

        Assert.That(_root.shortDescription, Is.EqualTo("Changed"));
        Assert.That(_scheduled, Is.Empty);
    }

    [Test]
    public void Checks_TreatAFailingCheckAsAMismatch()
    {
        _stillMatches = (_, _) => throw new InvalidOperationException("unreadable");

        StoredItemChanges.Changed(_root, restorable: true);
        RunScheduled();

        Assert.That(_forgotten, Is.EqualTo(new[] { _cell }));
    }

    [Test]
    public void Checks_AfterClose_DoNothing()
    {
        var forgotten = new List<object>();
        var scheduled = new List<Action>();
        var checks = new StoredRootChecks<object, object>(scheduled.Add, (_, _) => false, forgotten.Add, () => { });
        var cell = new object();

        checks.Mark(cell, new object());
        checks.Close();
        checks.Mark(cell, new object());
        foreach (var action in scheduled) action();

        Assert.That(scheduled, Has.Count.EqualTo(1));
        Assert.That(forgotten, Is.Empty);
    }

    private GameItem Stored(string id)
    {
        var item = new GameItem(id);
        _stored[item] = (_cell, item);
        return item;
    }

    private static ItemFeature Feature(GameItem item, string id, int owner = 0)
    {
        var feature = new ItemFeature(id) { parentItemUniqueId = owner };
        item.itemFeatures.Add(feature);
        return feature;
    }

    // Reports a change the way the patches do: before and after the native member runs.
    private static void Change(GameItem item, Action<GameItem> change)
    {
        StoredItemChanges.Changing(item);
        change(item);
        StoredItemChanges.Changed(item, restorable: true);
    }

    private void RunScheduled()
    {
        var actions = _scheduled.ToArray();
        _scheduled.Clear();
        foreach (var action in actions) action();
    }
}
