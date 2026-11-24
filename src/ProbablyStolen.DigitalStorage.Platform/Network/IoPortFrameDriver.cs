using System;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Network;

/// <summary>
/// Calls the IO Port coordinator once per frame, so a game second's transfers spread over the following frames. The
/// object lives for the mod's lifetime and survives scene changes; the composition destroys it on dispose.
/// </summary>
internal sealed class IoPortFrameDriver : MonoBehaviour
{
    private Action? _frame;
    private Action<Exception>? _report;
    private bool _reported;

    internal static IoPortFrameDriver Create(Action frame, Action<Exception> report)
    {
        var host = new GameObject("Digital Storage IO Port Frames");
        DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        var driver = host.AddComponent<IoPortFrameDriver>();
        (driver._frame, driver._report) = (frame, report);
        return driver;
    }

    internal void Destroy()
    {
        _frame = null;
        if (this != null && gameObject != null) UnityEngine.Object.Destroy(gameObject);
    }

    private void Update()
    {
        try { _frame?.Invoke(); }
        catch (Exception error)
        {
            // One report is enough to diagnose; the coordinator keeps its own state consistent per step.
            if (_reported) return;
            _reported = true;
            _report?.Invoke(error);
        }
    }
}
