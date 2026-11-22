using System;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class TerminalInventoryRefreshHandler : MonoBehaviour
{
    private Action? _poll;
    private Action? _shown;
    private Action? _hidden;

    internal void Initialize(Action poll, Action? shown = null, Action? hidden = null)
    {
        _poll = poll ?? throw new ArgumentNullException(nameof(poll));
        _shown = shown;
        _hidden = hidden;
        if (isActiveAndEnabled) _shown?.Invoke();
    }

    internal void Detach()
    {
        _poll = null;
        _shown = null;
        _hidden = null;
    }

    private void Update() => _poll?.Invoke();
    private void OnEnable() => _shown?.Invoke();
    private void OnDisable() => _hidden?.Invoke();
    private void OnDestroy() => Detach();
}
