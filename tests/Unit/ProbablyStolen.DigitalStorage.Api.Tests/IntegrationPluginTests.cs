using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Api.Tests;

[TestFixture]
public sealed class IntegrationPluginTests
{
    [Test]
    public void DefaultCallbacks_AcceptArgumentsWithoutAction()
    {
        var plugin = new TestPlugin();
        plugin.RegisterSettings(null!);
        plugin.Register(null!);
        plugin.OnRuntimeAvailable(null!);
        plugin.OnRuntimeUnavailable();
    }

    private sealed class TestPlugin : IntegrationPlugin
    {
        public override string PluginUid => "test";
        public override string RequiredModId => "test";
        public override IReadOnlyCollection<string> VerifiedVersions => Array.Empty<string>();
        public override string SettingSectionKey => "section";
        public override string SettingLabelKey => "label";
        public override string SettingTooltipKey => "tooltip";
    }
}
