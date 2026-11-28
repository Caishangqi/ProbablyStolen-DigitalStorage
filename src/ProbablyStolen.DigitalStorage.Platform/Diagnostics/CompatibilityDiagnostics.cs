using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Integration;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal sealed class CompatibilityDiagnostics
{
    private readonly DiagnosticLogger _log;
    private readonly LogCategory _category;

    internal CompatibilityDiagnostics(DiagnosticLogger log, LogCategory category) => (_log, _category) = (log, category);

    internal void Report(IntegrationNotice notice) => _log.Write(_category, notice.Severity switch
    {
        IntegrationSeverity.Error => LogVerbosity.Error,
        IntegrationSeverity.Warning => LogVerbosity.Warning,
        _ => LogVerbosity.Info
    }, "integration_status", "Compatibility integration state.", () => new[]
    {
        LogField.Of("plugin_id", notice.PluginId), LogField.Of("mod_id", notice.ModId),
        LogField.Of("version", notice.Version), LogField.Of("state", notice.State.ToString()),
        LogField.Of("verified", notice.Verified), LogField.Of("reason", notice.Reason),
        LogField.Of("decoration_id", notice.DecorationId)
    }, exceptionType: notice.ExceptionType);

    /// <summary>Gets the log one integration plugin writes its own records to.</summary>
    internal IIntegrationLog PluginLog(string pluginId) => new Plugin(_log, _category, pluginId);

    internal void ReportDecorationNotificationFailure(string exceptionType) =>
        _log.Write(_category, LogVerbosity.Error, "decoration_notification_failed",
            "Could not queue a terminal decoration refresh.", exceptionType: exceptionType);

    private sealed class Plugin : IIntegrationLog
    {
        private readonly DiagnosticLogger _log;
        private readonly LogCategory _category;
        private readonly string _pluginId;

        internal Plugin(DiagnosticLogger log, LogCategory category, string pluginId) =>
            (_log, _category, _pluginId) = (log, category, pluginId);

        public void Warning(string eventName, string text) => Write(LogVerbosity.Warning, eventName, text);

        public void Info(string eventName, string text) => Write(LogVerbosity.Info, eventName, text);

        private void Write(LogVerbosity level, string eventName, string text) =>
            _log.WriteText(_category, level, eventName ?? string.Empty, $"plugin_id={LogFormatter.Quote(_pluginId)} {text}");
    }
}
