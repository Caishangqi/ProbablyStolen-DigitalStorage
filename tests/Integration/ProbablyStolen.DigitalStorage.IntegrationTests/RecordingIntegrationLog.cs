using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

/// <summary>Keeps what a plugin writes to its compatibility log, one "event text" line per record.</summary>
internal sealed class RecordingIntegrationLog : IIntegrationLog
{
    internal List<string> Lines { get; } = new();

    public void Warning(string eventName, string text) => Lines.Add(eventName + " " + text);

    public void Info(string eventName, string text) => Lines.Add(eventName + " " + text);
}
