using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
internal sealed class ItemContentSynchronizationTests
{
    [Test]
    public void Definitions_UseTheApprovedFixedPricesForEveryEconomicItem()
    {
        var expected = new Dictionary<string, long>
        {
            [TerminalItem.ItemId] = 250,
            [DriveItem.ItemId] = 350,
            [ControllerItem.ItemId] = 400,
            [MaterialItem.ItemId] = 80,
            [CellWorkbenchItem.ItemId] = 300,
            [CellUpgradeInventory.EqualDistributionId] = 200,
            [CellUpgradeInventory.TypeLimitId] = 100
        };
        foreach (var tier in StorageCellTier.All)
        {
            expected.Add(tier.ComponentId, tier.ComponentValue);
            expected.Add(tier.CellId, 80);
        }

        Assert.That(ItemContentDefinitions.All.Keys, Is.EquivalentTo(expected.Keys));
        foreach (var (itemId, value) in expected)
            Assert.That(ItemContentDefinitions.All[itemId].UnitValue, Is.EqualTo(value), itemId);
    }

    [TestCase(TerminalItem.ItemId, TerminalItem.NameKey, TerminalItem.DescriptionKey, ItemPrices.Terminal)]
    [TestCase(DriveItem.ItemId, DriveItem.NameKey, DriveItem.DescriptionKey, ItemPrices.Drive)]
    [TestCase(ControllerItem.ItemId, ControllerItem.NameKey, ControllerItem.DescriptionKey, ItemPrices.Controller)]
    [TestCase(MaterialItem.ItemId, MaterialItem.NameKey, MaterialItem.DescriptionKey, ItemPrices.CellHousing)]
    [TestCase(StorageComponentFactory.ItemId, StorageComponentFactory.NameKey, StorageComponentFactory.DescriptionKey, ItemPrices.Component1K)]
    [TestCase(StorageCellItem.ItemId, StorageCellItem.NameKey, StorageCellItem.DescriptionKey, ItemPrices.CellHousing)]
    public void Synchronize_KnownId_UpdatesFourApprovedFields(string id, string nameKey, string descKey, long price)
    {
        var state = new State(id);
        var sync = CreateSync(key => "locale:" + key);

        sync.Synchronize(state);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(state.Name, Is.EqualTo("locale:" + nameKey));
            Assert.That(state.ShortDescription, Is.EqualTo("locale:" + descKey));
            Assert.That(state.FlavorText, Is.Empty);
            Assert.That(state.UnitValue, Is.EqualTo(price));
        }));
    }

    [Test]
    public void Synchronize_RepeatedAndUpdatedDefinitions_IsIdempotentAndUsesCurrentDefinitions()
    {
        var definition = new ItemContentDefinition("known", "name", "desc", 7);
        var definitions = new Dictionary<string, ItemContentDefinition> { ["known"] = definition };
        var state = new State("known");
        var sync = new ItemContentSynchronization(definitions, key => "first:" + key, _ => { });
        sync.Synchronize(state);
        var snapshot = state.Snapshot();
        var writes = state.Writes;
        sync.Synchronize(state);
        Assert.That(state.Snapshot(), Is.EqualTo(snapshot));
        Assert.That(state.Writes, Is.EqualTo(writes));

        definitions["known"] = new ItemContentDefinition("known", "name2", "desc2", 9);
        var updated = new ItemContentSynchronization(definitions, key => "second:" + key, _ => { });
        updated.Synchronize(state);
        Assert.That(state.Name, Is.EqualTo("second:name2"));
        Assert.That(state.ShortDescription, Is.EqualTo("second:desc2"));
        Assert.That(state.UnitValue, Is.EqualTo(9));
    }

    [Test]
    public void Synchronize_UnknownId_LeavesEveryFieldAndChildGraphUnchanged()
    {
        var state = new State("unknown") { Name = "custom", ShortDescription = "old", FlavorText = "lore", UnitValue = 99 };
        var sync = CreateSync(key => key);
        var snapshot = state.Snapshot();

        sync.Synchronize(state);

        Assert.That(state.Snapshot(), Is.EqualTo(snapshot));
    }

    [Test]
    public void Synchronize_MissingLocalizedText_KeepsTextTripleAndStillUpdatesPrice()
    {
        var state = new State(TerminalItem.ItemId) { Name = "old name", ShortDescription = "old desc", FlavorText = "old flavor", UnitValue = 999 };
        var errors = new List<string>();
        var sync = new ItemContentSynchronization(key => key == TerminalItem.NameKey ? "localized name" : key,
            errors.Add);

        sync.Synchronize(state);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(state.Name, Is.EqualTo("old name"));
            Assert.That(state.ShortDescription, Is.EqualTo("old desc"));
            Assert.That(state.FlavorText, Is.EqualTo("old flavor"));
            Assert.That(state.UnitValue, Is.EqualTo(ItemPrices.Terminal));
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Not.Contain("old").And.Not.Contain(TerminalItem.ItemId));
        }));
    }

    [Test]
    public void Synchronize_ResolverLocaleModel_DoesNotTouchSyntheticState()
    {
        var state = new State(StorageCellItem.ItemId)
        {
            Name = "custom name",
            ShortDescription = "old",
            FlavorText = "obsolete flavor",
            UnitValue = 0,
            CustomNameTag = "player label",
            OtherTag = 123,
            Child = new object()
        };
        var locale = "fr";
        string Resolve(string key) => locale == "fr" ? "english:" + key : "new:" + key;
        var sync = CreateSync(Resolve);

        sync.Synchronize(state);
        Assert.That(state.Name, Is.EqualTo("english:" + StorageCellItem.NameKey));
        locale = "en";
        sync.Synchronize(state);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(state.Name, Is.EqualTo("new:" + StorageCellItem.NameKey));
            Assert.That(state.FlavorText, Is.Empty);
            Assert.That(state.CustomNameTag, Is.EqualTo("player label"));
            Assert.That(state.OtherTag, Is.EqualTo(123));
            Assert.That(state.Child, Is.Not.Null);
        }));
    }

    [Test]
    public void Synchronize_LoggingFailure_DoesNotEscapeCallbackBoundary()
    {
        var state = new State(TerminalItem.ItemId) { Name = "old", ShortDescription = "old", FlavorText = "old" };
        var sync = new ItemContentSynchronization(_ => throw new InvalidOperationException("lookup"),
            _ => throw new InvalidOperationException("logger"));

        Assert.DoesNotThrow((Action)(() => sync.Synchronize(state)));
        Assert.That(state.UnitValue, Is.EqualTo(ItemPrices.Terminal));
        Assert.That(state.Name, Is.EqualTo("old"));
    }

    [Test]
    public void LoadCallback_Disable_MakesFutureInvocationsInert()
    {
        var state = new State(TerminalItem.ItemId);
        var sync = CreateSync(key => "first:" + key);
        var callback = new ItemContentLoadCallback(sync, _ => { });

        callback.Invoke(state);
        callback.Disable();
        state.Name = "saved";
        callback.Invoke(state);

        Assert.That(state.Name, Is.EqualTo("saved"));
    }

    [Test]
    public void Synchronize_DefinedFlavor_ResolvesAndAppliesFlavor()
    {
        var definitions = new Dictionary<string, ItemContentDefinition>
        {
            ["known"] = new("known", "name", "desc", 7, "flavor")
        };
        var state = new State("known");
        new ItemContentSynchronization(definitions, key => "translated:" + key, _ => { }).Synchronize(state);
        Assert.That(state.FlavorText, Is.EqualTo("translated:flavor"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Synchronize_MissingOrThrowingFlavor_RetainsTripleAndBoundsDiagnostics(bool throws)
    {
        var definitions = new Dictionary<string, ItemContentDefinition>
        {
            ["known"] = new("known", "name", "desc", 7, "flavor")
        };
        var errors = new List<string>();
        string Resolve(string key) => key == "flavor" ? (throws ? throw new InvalidOperationException() : key) : "translated:" + key;
        var sync = new ItemContentSynchronization(definitions, Resolve, errors.Add);
        var state = new State("known") { Name = "old", ShortDescription = "old desc", FlavorText = "old flavor" };
        sync.Synchronize(state);
        state.UnitValue = 99;
        sync.Synchronize(state);
        Assert.That(state.Name, Is.EqualTo("old"));
        Assert.That(state.ShortDescription, Is.EqualTo("old desc"));
        Assert.That(state.FlavorText, Is.EqualTo("old flavor"));
        Assert.That(state.UnitValue, Is.EqualTo(7));
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadCallback_ThrowingState_ContainsAndAllowsLaterGoodInvocation()
    {
        var errors = new List<string>();
        var callback = new ItemContentLoadCallback(CreateSync(key => "value:" + key), errors.Add);
        var state = new State(TerminalItem.ItemId) { ThrowOnRead = true };
        Assert.DoesNotThrow((Action)(() => callback.Invoke(state)));
        state.ThrowOnRead = false;
        callback.Invoke(state);
        Assert.That(state.Name, Is.EqualTo("value:" + TerminalItem.NameKey));
        Assert.That(state.UnitValue, Is.EqualTo(ItemPrices.Terminal));
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadCallback_DisabledBeforeInvocation_DoesNotReadResolveOrWrite()
    {
        var resolutions = 0;
        var state = new State(TerminalItem.ItemId) { ThrowOnRead = true, ThrowOnWrite = true };
        var callback = new ItemContentLoadCallback(CreateSync(key => { resolutions++; return key; }), _ => { });
        callback.Disable();
        Assert.DoesNotThrow((Action)(() => callback.Invoke(state)));
        Assert.That(resolutions, Is.Zero);
        Assert.That(state.Writes, Is.Zero);
    }

    [Test]
    public void Synchronize_TextOnly_LeavesPriceAndReportsChanged()
    {
        var state = new State(TerminalItem.ItemId) { UnitValue = 987 };
        var result = CreateSync(key => "localized:" + key).Synchronize(state, textOnly: true);
        Assert.That(result, Is.EqualTo(ItemContentResult.Changed));
        Assert.That(state.UnitValue, Is.EqualTo(987));
    }

    [Test]
    public void Synchronize_TextSetterFailure_RestoresThePreviouslyWrittenFields()
    {
        var state = new State(TerminalItem.ItemId)
        {
            Name = "old name", ShortDescription = "old desc", FlavorText = "old flavor", UnitValue = 987,
            ThrowOnDescriptionWriteOnce = true
        };
        var sync = CreateSync(key => "localized:" + key);

        Assert.That(sync.Synchronize(state), Is.EqualTo(ItemContentResult.Failed));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(state.Name, Is.EqualTo("old name"));
            Assert.That(state.ShortDescription, Is.EqualTo("old desc"));
            Assert.That(state.FlavorText, Is.EqualTo("old flavor"));
            Assert.That(state.UnitValue, Is.EqualTo(ItemPrices.Terminal), "A text failure does not block the price update.");
        }));
    }

    [Test]
    public void Synchronize_InvalidPrice_KeepsValueAndReportsFailureWhileTextChanges()
    {
        var definitions = new Dictionary<string, ItemContentDefinition> { ["known"] = new("known", "name", "desc", 7) };
        var state = new State("known") { UnitValue = 987 };
        var errors = new List<string>();
        var sync = new ItemContentSynchronization(definitions, key => "localized:" + key, errors.Add,
            (_, _) => throw new InvalidOperationException());
        Assert.That(sync.Synchronize(state), Is.EqualTo(ItemContentResult.Failed));
        Assert.That(state.UnitValue, Is.EqualTo(987));
        Assert.That(state.Name, Is.EqualTo("localized:name"));
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    private static ItemContentSynchronization CreateSync(Func<string, string> resolve) =>
        new(ItemContentDefinitions.All, resolve, _ => { });

    private sealed class State : IItemContentState
    {
        private readonly string _id;
        private string? _name;
        private string? _description;
        private string? _flavor;
        private long _unitValue;
        internal State(string id) => _id = id;
        internal bool ThrowOnRead { get; set; }
        internal bool ThrowOnWrite { get; set; }
        internal bool ThrowOnDescriptionWriteOnce { get; set; }
        internal int Writes { get; private set; }
        public string? Identifier => ThrowOnRead ? throw new InvalidOperationException() : _id;
        public string? Name { get => _name; set { CheckWrite(); _name = value; } }
        public string? ShortDescription
        {
            get => _description;
            set
            {
                if (ThrowOnDescriptionWriteOnce)
                {
                    ThrowOnDescriptionWriteOnce = false;
                    throw new InvalidOperationException();
                }
                CheckWrite();
                _description = value;
            }
        }
        public string? FlavorText { get => _flavor; set { CheckWrite(); _flavor = value; } }
        public long UnitValue { get => _unitValue; set { CheckWrite(); _unitValue = value; } }
        private void CheckWrite()
        {
            if (ThrowOnWrite) throw new InvalidOperationException();
            Writes++;
        }
        internal string? CustomNameTag { get; set; }
        internal int OtherTag { get; set; }
        internal object? Child { get; set; }
        internal string Snapshot() => $"{Name}|{ShortDescription}|{FlavorText}|{UnitValue}|{CustomNameTag}|{OtherTag}|{Child}";
    }
}
