using System;
using ProbablyStolen.DigitalStorage.Common.Initialization;

namespace ProbablyStolen.DigitalStorage.Platform.Localization;

internal sealed class ModText
{
    private readonly string _modId;

    internal ModText(ModuleIdentity identity)
    {
        _modId = (identity ?? throw new ArgumentNullException(nameof(identity))).Id;
    }

    internal string Get(string key, params object[] args)
    {
        return ModHelper.GetLocalized(_modId, key, args);
    }
}
