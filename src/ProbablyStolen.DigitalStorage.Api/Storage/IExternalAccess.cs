using System;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;

namespace ProbablyStolen.DigitalStorage.Api.Storage;

/// <summary>
/// A scoped permission for another mod's own item scan. Outside a scope, items stored in cells refuse native removal
/// checks (<c>GameItem.MayRemove</c>, <c>MaxNumRemove</c>), so other code cannot see them as removable. Inside a
/// scope, roots in online networks report as removable, so a scan that respects those checks finds them.
/// </summary>
/// <remarks>
/// A scope only changes what the native checks report; it does not move anything. The caller must still move each
/// stored root through <see cref="IStorage.ExtractRoot"/>, never through native moves. Scopes are main-thread only,
/// must be disposed in the same call that opened them, and close the cell pages they touched (empty pages are
/// reclaimed on dispose).
/// </remarks>
public interface IExternalAccess
{
    /// <summary>Gets whether a scope is open.</summary>
    bool IsOpen { get; }

    /// <summary>Opens a scope for the given source. Dispose the result to close it.</summary>
    IDisposable Open(IActionSource source);

    /// <summary>Gets whether a batch is running.</summary>
    bool IsBatching { get; }

    /// <summary>
    /// Starts a batch of API moves that may span several frames, for example a caller that defers inventory callbacks
    /// while it places items. A batch grants no removal permission; it only postpones the reclaiming of empty cell pages
    /// until the batch ends, so pages are not replaced while the caller's own deferred work still refers to them.
    /// Batches do not nest: starting one while another runs ends the earlier one. Dispose the result to end it.
    /// </summary>
    IDisposable Batch(IActionSource source);
}
