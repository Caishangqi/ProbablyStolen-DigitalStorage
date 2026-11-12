using System.Threading;

namespace ProbablyStolen.DigitalStorage.Platform.Api;

/// <summary>Remembers the thread that composed the mod (the Unity main thread) and answers whether a call runs on it.</summary>
internal sealed class ApiThread
{
    private readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;

    /// <summary>Gets whether the caller runs on the main thread.</summary>
    internal bool IsMain => Thread.CurrentThread.ManagedThreadId == _mainThreadId;
}
