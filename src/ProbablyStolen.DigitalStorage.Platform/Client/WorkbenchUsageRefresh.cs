using System;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class WorkbenchUsageRefresh : MonoBehaviour
{
    private WorkbenchUsage.Slot? _slot;
    private Action? _render;

    internal void Bind(WorkbenchUsage.Slot slot, Action render)
    {
        _slot = slot;
        _render = render;
    }

    private void LateUpdate()
    {
        if (_slot?.Refresh() == true) _render?.Invoke();
    }

    private void OnDisable() => _slot?.Invalidate();

    private void OnDestroy()
    {
        _slot?.Detach();
        _slot = null;
        _render = null;
    }
}
