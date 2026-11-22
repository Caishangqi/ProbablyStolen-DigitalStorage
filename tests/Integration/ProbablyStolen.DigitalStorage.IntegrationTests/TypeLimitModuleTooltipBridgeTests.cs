using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Mod.Patches;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
[NonParallelizable]
public sealed class TypeLimitModuleTooltipBridgeTests
{
    private const string Effect = ModuleHelper.ADDITIONAL_EFFECT_STRING;
    private const string English = "Common: type limit +80 and +5%";
    private const string Chinese = "普通: 种类上限 +80，+5%";

    [Test]
    public void RollAndPresent_ExistingAndNewModulesKeepTheirRawTagsAndPriceAcrossRefreshes()
    {
        var text = new ModText { Translation = TranslateEnglish };
        var errors = new List<string>();
        using var registration = TypeLimitModuleTooltipBridge.Register(text, errors.Add);
        var old = LegacyModule();
        var fresh = new GameItem(CellUpgradeInventory.TypeLimitId);
        var appearance = new ItemAppearance();
        var changes = new List<GameItem>();
        var factory = new CellModuleFactory(appearance, changes.Add, text, errors.Add);
        factory.Roll(fresh, TypeLimitRolls.Roll(ModuleQuality.Common, new MinimumRandom()));
        Assert.That(changes, Is.EqualTo(new[] { fresh }));
        Assert.That(fresh.GetTagReadonly(Effect).GetString(), Is.EqualTo("Common: type limit +8 and +5%"));

        foreach (var item in new[] { old, fresh })
        {
            var state = item.state;
            var modified = item.modifiedState;
            var tags = new Dictionary<string, TagState>(state.dict);
            var storedEffect = state.dict[Effect].GetString();
            var writes = item.TagWrites;
            foreach (var (translate, expected) in new (Func<string, string?>, string)[]
                { (TranslateEnglish, English), (TranslateChinese, Chinese), (TranslateEnglish, English) })
            {
                text.Translation = translate;
                factory.Present(item);
                Assert.That(appearance.Sprite, Is.EqualTo("module_type_expansion_common"));
                Assert.That(Draw(item), Is.EqualTo(new[] { expected }));
                Assert.That(Draw(item), Is.EqualTo(new[] { expected }));
                Assert.That(item.state, Is.SameAs(state));
                Assert.That(item.modifiedState, Is.SameAs(modified));
                Assert.That(item.state.dict, Is.EqualTo(tags));
                Assert.That(item.state.dict[Effect].GetString(), Is.EqualTo(storedEffect));
                Assert.That(item.TagWrites, Is.EqualTo(writes));
            }
            var roll = ModuleTags.Read(item)!;
            Assert.That((roll.Quality, roll.Add, roll.EffectiveAdd, roll.Percent), Is.EqualTo((ModuleQuality.Common, 8, 80L, 5)));
            Assert.That(TypeLimitRolls.Price(roll), Is.EqualTo(80));
            Assert.That(CellTypeLimit.Calculate(StorageCellTier.All[0], UpgradeInventory.Empty.With(InstalledModule.TypeLimit(roll))),
                Is.EqualTo((352, 142)));
            var spy = CellPages.Spy(item);
            Assert.That((spy.LayoutChecks, spy.CachedReads, spy.LiveReads), Is.EqualTo((0, 0, 0)));
        }
        Assert.That(errors, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Begin_MalformedOrMissingRollShowsOnlyTheLocalizedInvalidLine(bool missing)
    {
        using var registration = TypeLimitModuleTooltipBridge.Register(new ModText { Translation = TranslateEnglish }, ReportError);
        var item = LegacyModule();
        if (missing) item.state.dict.Remove(ModuleTags.QualityTag);
        else item.state.dict[ModuleTags.AddTag].SetInt(80);
        item.modifiedState = item.state.Clone();

        Assert.That(Draw(item), Is.EqualTo(new[] { "Unreadable module: no effect" }));
        Assert.That(item.state.dict[Effect].GetString(), Is.EqualTo("old effect"));
    }

    [Test]
    public void Begin_LeavesNonTargetsAndInactiveRegistrationAlone()
    {
        var unrelated = new GameItem(CellUpgradeInventory.EqualDistributionId);
        unrelated.ModifyTag(Effect, tag => tag.Enable().SetString("unrelated"));
        using (TypeLimitModuleTooltipBridge.Register(new ModText { Translation = TranslateEnglish }, ReportError))
        {
            Assert.That(TypeLimitModuleTooltipBridge.Begin(unrelated), Is.Null);
            Assert.That(Draw(unrelated), Is.EqualTo(new[] { "unrelated" }));
        }
        Assert.That(TypeLimitModuleTooltipBridge.Begin(LegacyModule()), Is.Null);
    }

    [Test]
    public void Finalizer_RestoresTheOuterProjectionAfterAnInnerErrorAndTheOriginalAfterAnOuterError()
    {
        var text = new ModText { Translation = TranslateEnglish };
        using var registration = TypeLimitModuleTooltipBridge.Register(text, ReportError);
        var module = LegacyModule();
        var original = module.modifiedState;
        var innerError = new InvalidOperationException("inner");
        var outerError = new InvalidOperationException("outer");
        var builder = new RichTextBuilder();
        builder.OnAdd = _ =>
        {
            var outer = module.modifiedState;
            Assert.That(outer, Is.Not.SameAs(original));
            Assert.That(outer.dict[Effect], Is.Not.SameAs(original.dict[Effect]));
            text.Translation = TranslateChinese;
            var inner = new RichTextBuilder { OnAdd = _ => throw innerError };
            Assert.That(Assert.Throws<InvalidOperationException>(new Action(() => Draw(module, inner))), Is.SameAs(innerError));
            Assert.That(inner.Lines, Is.EqualTo(new[] { Chinese }));
            Assert.That(module.modifiedState, Is.SameAs(outer));
            Assert.That(outer.dict[Effect].GetString(), Is.EqualTo(English));
            throw outerError;
        };

        Assert.That(Assert.Throws<InvalidOperationException>(new Action(() => Draw(module, builder))), Is.SameAs(outerError));
        Assert.That(module.modifiedState, Is.SameAs(original));
        Assert.That(original.dict[Effect].GetString(), Is.EqualTo("old effect"));
    }

    [TestCase("missing")]
    [TestCase("malformed")]
    [TestCase("incomplete")]
    [TestCase("escaped")]
    [TestCase("throw")]
    public void LocalizationFailure_FallsBackToTheEffectiveLineAndKeepsNewRawEffects(string failure)
    {
        var text = new ModText();
        text.Translation = key => key != "module_type_limit_effect" ? TranslateEnglish(key) : failure switch
        {
            "missing" => null,
            "malformed" => "{0}: +{1} and +{2}% {",
            "incomplete" => "{0}: +{1}",
            "escaped" => "{0}: type limit +{{1}} and +{2}%",
            _ => throw new InvalidOperationException("translation failed")
        };
        var errors = new List<string>();
        using var registration = TypeLimitModuleTooltipBridge.Register(text, errors.Add);
        var module = LegacyModule();
        var original = module.modifiedState;
        Assert.That(Draw(module), Is.EqualTo(new[] { English }));
        Assert.That(Draw(module), Is.EqualTo(new[] { English }));
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(module.modifiedState, Is.SameAs(original));
        Assert.That(module.state.dict[Effect].GetString(), Is.EqualTo("old effect"));

        var fresh = new GameItem(CellUpgradeInventory.TypeLimitId);
        var factory = new CellModuleFactory(new ItemAppearance(), _ => { }, text, errors.Add);
        factory.Roll(fresh, new TypeLimitRoll(ModuleQuality.Common, 8, 5));
        Assert.That(fresh.state.dict[Effect].GetString(), Is.EqualTo("Common: type limit +8 and +5%"));
        Assert.That(Draw(fresh), Is.EqualTo(new[] { English }));
        Assert.That(errors, Has.Count.EqualTo(2));
    }

    [Test]
    public void Begin_ProjectsOverTheViewInstalledByALocalizationErrorCallback()
    {
        var item = LegacyModule();
        var replacement = item.modifiedState.Clone();
        replacement.GetTag("unrelated").Enable().SetString("keep me");
        using var registration = TypeLimitModuleTooltipBridge.Register(new ModText { Translation = _ => null },
            _ => item.modifiedState = replacement);
        var builder = new RichTextBuilder
        {
            OnAdd = _ => Assert.That(item.modifiedState.dict["unrelated"].GetString(), Is.EqualTo("keep me"))
        };

        Assert.That(Draw(item, builder), Is.EqualTo(new[] { English }));
        Assert.That(item.modifiedState, Is.SameAs(replacement));
        Assert.That(replacement.dict[Effect].GetString(), Is.EqualTo("old effect"));
        Assert.That(item.state.dict[Effect].GetString(), Is.EqualTo("old effect"));
    }

    [Test]
    public void Begin_FallsBackToOneInvalidLineWhenTheInvalidTranslationIsMissing()
    {
        using var registration = TypeLimitModuleTooltipBridge.Register(new ModText { Translation = _ => null }, ReportError);
        var item = LegacyModule();
        item.modifiedState.dict.Remove(ModuleTags.AddTag);
        Assert.That(Draw(item), Is.EqualTo(new[] { "Unreadable module: no effect" }));
        Assert.That(item.state.dict[Effect].GetString(), Is.EqualTo("old effect"));
    }

    [Test]
    public void Finalizer_KeepsAnIndependentModifiedStateReplacement()
    {
        using var registration = TypeLimitModuleTooltipBridge.Register(new ModText { Translation = TranslateEnglish }, ReportError);
        var item = LegacyModule();
        var replacement = item.modifiedState.Clone();
        var effect = new TagState().Enable().SetString("other mod's effect");
        replacement.dict[Effect] = effect;
        var builder = new RichTextBuilder { OnAdd = _ => item.modifiedState = replacement };
        Assert.That(Draw(item, builder), Is.EqualTo(new[] { English }));
        Assert.That(item.modifiedState, Is.SameAs(replacement));
        Assert.That(replacement.dict[Effect], Is.SameAs(effect));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Finalizer_RestoresOnlyTheProjectedEntryInAShallowClone(bool hadOriginalEffect)
    {
        using var registration = TypeLimitModuleTooltipBridge.Register(new ModText { Translation = TranslateEnglish }, ReportError);
        var item = LegacyModule();
        var original = item.modifiedState;
        if (!hadOriginalEffect) original.dict.Remove(Effect);
        TagSystem? replacement = null;
        var builder = new RichTextBuilder
        {
            OnAdd = _ =>
            {
                replacement = item.modifiedState.Clone();
                Assert.That(replacement.dict[Effect], Is.SameAs(item.modifiedState.dict[Effect]));
                replacement.GetTag("unrelated").Enable().SetString("keep me");
                item.modifiedState = replacement;
            }
        };
        Assert.That(Draw(item, builder), Is.EqualTo(new[] { English }));
        Assert.That(item.modifiedState, Is.SameAs(replacement));
        Assert.That(replacement!.dict["unrelated"].GetString(), Is.EqualTo("keep me"));
        if (hadOriginalEffect) Assert.That(replacement.dict[Effect], Is.SameAs(original.dict[Effect]));
        else Assert.That(replacement.dict.ContainsKey(Effect), Is.False);
        Assert.That(item.state.dict[Effect].GetString(), Is.EqualTo("old effect"));
    }

    [Test]
    public void Finalizer_UnwindsBothEntriesWhenAnInnerTooltipLeavesAShallowClone()
    {
        var text = new ModText { Translation = TranslateEnglish };
        using var registration = TypeLimitModuleTooltipBridge.Register(text, ReportError);
        var item = LegacyModule();
        var oldEffect = item.modifiedState.dict[Effect];
        TagSystem? replacement = null;
        var builder = new RichTextBuilder();
        builder.OnAdd = _ =>
        {
            var outerEffect = item.modifiedState.dict[Effect];
            text.Translation = TranslateChinese;
            var inner = new RichTextBuilder
            {
                OnAdd = _ => item.modifiedState = replacement = item.modifiedState.Clone()
            };
            Assert.That(Draw(item, inner), Is.EqualTo(new[] { Chinese }));
            Assert.That(item.modifiedState, Is.SameAs(replacement));
            Assert.That(replacement!.dict[Effect], Is.SameAs(outerEffect));
        };
        Assert.That(Draw(item, builder), Is.EqualTo(new[] { English }));
        Assert.That(item.modifiedState, Is.SameAs(replacement));
        Assert.That(replacement!.dict[Effect], Is.SameAs(oldEffect));
    }

    private static List<string> Draw(GameItem item, RichTextBuilder? builder = null)
    {
        builder ??= new RichTextBuilder();
        TypeLimitModuleTooltipPatch.Prefix(item, out var scope);
        Exception? failure = null;
        try { ModuleHelper.CreateModuleTooltip(builder, item); }
        catch (Exception error) { failure = error; throw; }
        finally { Assert.That(TypeLimitModuleTooltipPatch.Finalizer(failure, scope), Is.SameAs(failure)); }
        return builder.Lines;
    }

    private static GameItem LegacyModule()
    {
        var item = new GameItem(CellUpgradeInventory.TypeLimitId);
        item.ModifyTag(ModuleTags.QualityTag, tag => tag.Enable().SetString("common"));
        item.ModifyTag(ModuleTags.AddTag, tag => tag.Enable().SetInt(8));
        item.ModifyTag(ModuleTags.PercentTag, tag => tag.Enable().SetInt(5));
        item.ModifyTag(Effect, tag => tag.Enable().SetString("old effect"));
        return item;
    }

    private static void ReportError(string message) =>
        TestContext.Error.WriteLine(message);

    private static string? TranslateEnglish(string key) => key switch
    {
        "module_quality_common" => "Common",
        "module_type_limit_effect" => "{0}: type limit +{1} and +{2}%",
        "module_type_limit_invalid" => "Unreadable module: no effect",
        _ => null
    };

    private static string? TranslateChinese(string key) => key switch
    {
        "module_quality_common" => "普通",
        "module_type_limit_effect" => "{0}: 种类上限 +{1}，+{2}%",
        _ => null
    };

    private sealed class MinimumRandom : IRandom
    {
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }
}
