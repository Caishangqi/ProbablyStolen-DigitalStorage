using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>Reports the reasons <see cref="StorageService"/> recorded for fallback keys this session.</summary>
internal sealed class StorageIdentityDiagnostics : IItemIdentityDiagnostics
{
    internal static readonly StorageIdentityDiagnostics Instance = new();

    private StorageIdentityDiagnostics()
    {
    }

    public ItemIdentityFallback? FallbackOf(ItemKey key) =>
        key != null && StorageService.TryGetFallbackReason(key, out var reason)
            ? new ItemIdentityFallback(reason.Failure.ToString(), reason.Rule, reason.At)
            : null;
}
