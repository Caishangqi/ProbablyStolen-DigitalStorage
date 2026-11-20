namespace ProbablyStolen.DigitalStorage.Api.Integration;

/// <summary>Uses a shared settings key for versions the plugin can probe for compatibility.</summary>
internal interface IIntegrationVersionPolicy
{
    bool SupportsVersion(string version);
}
