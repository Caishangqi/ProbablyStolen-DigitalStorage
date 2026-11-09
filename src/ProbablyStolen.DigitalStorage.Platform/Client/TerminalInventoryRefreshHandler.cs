using System;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class TerminalInventoryRefreshHandler : MonoBehaviour
{
    private Action? _poll;
    internal void Initialize(Action poll) => _poll = poll ?? throw new ArgumentNullException(nameof(poll));
    private void Update() => _poll?.Invoke();
    private void OnDestroy() => _poll = null;
}
