using System;
using System.Collections.Generic;
using System.Reflection;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Platform.Integration;

/// <summary>Collects what one plugin requests in its register phase; the host applies it afterwards.</summary>
internal sealed class IntegrationRegistration : ITerminalDecorationRegistration
{
    private static readonly System.Text.RegularExpressions.Regex DecorationId =
        new("^[a-z0-9_.-]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex OverlayId =
        new("^[a-z0-9_.-]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly Action Inactive = () => { };
    private readonly string _pluginId;
    private InvalidationScope? _invalidation;
    private readonly List<IntegrationDecorationCatalog.Contribution> _decorations = new();
    private readonly HashSet<string> _decorationIds = new(StringComparer.Ordinal);
    private readonly List<PatchRequest> _patches = new();
    private readonly List<Func<string?>> _checks = new();

    internal IntegrationRegistration(string detectedModVersion, IDigitalStorageRuntime runtime, string pluginId = "",
        IIntegrationLog? log = null)
    {
        DetectedModVersion = detectedModVersion ?? string.Empty;
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _pluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
        Log = log ?? SilentIntegrationLog.Instance;
    }

    /// <inheritdoc />
    public string DetectedModVersion { get; }

    /// <inheritdoc />
    public IDigitalStorageRuntime Runtime { get; }

    /// <inheritdoc />
    public IIntegrationLog Log { get; }

    /// <summary>Gets the first reason the plugin cannot work, or null.</summary>
    internal string? Failure { get; private set; }

    internal IReadOnlyList<PatchRequest> Patches => _patches;

    internal IReadOnlyList<Func<string?>> Checks => _checks;

    internal IReadOnlyList<IntegrationDecorationCatalog.Contribution> Decorations => _decorations;

    /// <inheritdoc />
    public Action InvalidateSlotDecorations => _invalidation?.Callback ?? Inactive;

    internal void BeginDecorationScope(Action invalidate)
    {
        EndDecorationScope();
        _invalidation = new InvalidationScope(invalidate);
    }

    internal void EndDecorationScope()
    {
        _invalidation?.Dispose();
        _invalidation = null;
    }

    public void AddSlotDecoration(string id, string overlayKey,
        Func<IReadOnlyList<GameItem>, IReadOnlyList<int?>> evaluate)
    {
        if (!DecorationId.IsMatch(id ?? string.Empty)) throw new ArgumentException("A lowercase decoration id is required.", nameof(id));
        if (!OverlayId.IsMatch(overlayKey ?? string.Empty)) throw new ArgumentException("A local texture key is required.", nameof(overlayKey));
        if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));
        var scopedId = _pluginId + "/" + id;
        if (!_decorationIds.Add(scopedId)) throw new ArgumentException("The decoration id is already registered by this plugin.", nameof(id));
        _decorations.Add(new IntegrationDecorationCatalog.Contribution(scopedId, overlayKey!, evaluate));
    }

    /// <inheritdoc />
    public void Patch(MethodBase? target, MethodInfo? prefix = null, MethodInfo? postfix = null,
        MethodInfo? transpiler = null, MethodInfo? finalizer = null)
    {
        if (target == null)
        {
            Fail("a patch target was not found");
            return;
        }

        if (prefix == null && postfix == null && transpiler == null && finalizer == null)
        {
            Fail($"a patch of {target.Name} names no patch method");
            return;
        }

        _patches.Add(new PatchRequest(target, prefix, postfix, transpiler, finalizer));
    }

    /// <inheritdoc />
    public void Fail(string reason) => Failure ??= string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;

    /// <inheritdoc />
    public void Verify(Func<string?> check) => _checks.Add(check ?? throw new ArgumentNullException(nameof(check)));

    private sealed class InvalidationScope : IDisposable
    {
        private Action? _invalidate;

        internal InvalidationScope(Action invalidate)
        {
            _invalidate = invalidate;
            Callback = () => _invalidate?.Invoke();
        }

        internal Action Callback { get; }

        public void Dispose() => _invalidate = null;
    }
}

/// <summary>A plugin log that writes nothing, for hosts without a compatibility log.</summary>
internal sealed class SilentIntegrationLog : IIntegrationLog
{
    internal static readonly SilentIntegrationLog Instance = new();

    public void Warning(string eventName, string text)
    {
    }

    public void Info(string eventName, string text)
    {
    }
}
