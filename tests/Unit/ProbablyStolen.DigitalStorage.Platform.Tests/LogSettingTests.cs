using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
internal sealed class LogSettingTests
{
    [Test]
    public void Register_BuiltinsHaveStableKeysDefaultsAndDeclarationOrder()
    {
        var categories = new BuiltinLogCategories();
        Assert.That(categories.Registry.Entries, Is.EqualTo(new[] { categories.Storage, categories.Transfer, categories.Temp }));
        Assert.That(new[] { categories.Storage.Name, categories.Transfer.Name, categories.Temp.Name },
            Is.EqualTo(new[] { "LogStorage", "LogTransfer", "LogTemp" }));
        Assert.That(categories.Storage.DefaultEnabled && categories.Transfer.DefaultEnabled, Is.True);
        Assert.That(categories.Storage.DefaultVerbosity, Is.EqualTo(LogVerbosity.Info));
        Assert.That(categories.Transfer.DefaultVerbosity, Is.EqualTo(LogVerbosity.Info));
        Assert.That(categories.Temp.DefaultEnabled, Is.False);
        Assert.That(categories.Temp.DefaultVerbosity, Is.EqualTo(LogVerbosity.Verbose));
        var setting = new LogSetting(categories.Transfer);
        Assert.That(setting.VerbosityKey, Is.EqualTo("log_transfer_verbosity"));
        Assert.That(setting.EnabledKey, Is.EqualTo("log_transfer_enabled"));
    }

    [Test]
    public void Label_FallsBackForNullOrMissingTranslations()
    {
        var categories = new BuiltinLogCategories();
        var setting = new LogSetting(categories.Storage);
        Assert.That(setting.Label(key => key), Is.EqualTo("Storage log"));
        Assert.That(setting.Label(_ => null), Is.EqualTo("Storage log"));
        Assert.That(setting.Label(_ => "Localized label"), Is.EqualTo("Localized label"));
        var noKey = new LogSetting(new LogCategories().Register("log_extra", null, "Fallback", LogVerbosity.Info, true));
        Assert.That(noKey.Label(_ => throw new InvalidOperationException()), Is.EqualTo("Fallback"));
    }

    [Test]
    public void DisplayedIndex_UsesNativeChoiceOrderAndInvalidValueUsesCategoryDefault()
    {
        var categories = new BuiltinLogCategories();
        var choices = new[] { "Warning", "Info", "Verbose", "Error" };
        var setting = new LogSetting(categories.Storage);
        Assert.That(setting.DisplayedIndex(LogVerbosity.Error, 3, choices), Is.EqualTo(3));
        Assert.That(setting.DisplayedIndex((LogVerbosity)105, 0, choices), Is.EqualTo(1));
        Assert.That(setting.Normalize((LogVerbosity)105), Is.EqualTo(LogVerbosity.Info));
        Assert.That(new LogSetting(categories.Temp).DisplayedIndex((LogVerbosity)105, 0, choices), Is.EqualTo(2));
    }
}
