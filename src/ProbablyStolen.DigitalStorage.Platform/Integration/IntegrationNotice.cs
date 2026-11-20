namespace ProbablyStolen.DigitalStorage.Platform.Integration;

internal enum IntegrationSeverity
{
    Info,
    Warning,
    Error
}

internal readonly struct IntegrationNotice
{
    internal IntegrationNotice(IntegrationSeverity severity, string pluginId, string modId, string version,
        IntegrationState state, bool verified, string reason, string? exceptionType = null)
    {
        Severity = severity;
        PluginId = pluginId;
        ModId = modId;
        Version = version;
        State = state;
        Verified = verified;
        Reason = reason;
        ExceptionType = exceptionType;
    }

    internal IntegrationSeverity Severity { get; }
    internal string PluginId { get; }
    internal string ModId { get; }
    internal string Version { get; }
    internal IntegrationState State { get; }
    internal bool Verified { get; }
    internal string Reason { get; }
    internal string? ExceptionType { get; }
}
