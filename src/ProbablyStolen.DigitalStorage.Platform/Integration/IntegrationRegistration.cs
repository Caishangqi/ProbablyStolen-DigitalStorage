using System;
using System.Collections.Generic;
using System.Reflection;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Platform.Integration;

/// <summary>Collects what one plugin requests in its register phase; the host applies it afterwards.</summary>
internal sealed class IntegrationRegistration : IIntegrationRegistration
{
    private readonly List<PatchRequest> _patches = new();
    private readonly List<Func<string?>> _checks = new();

    internal IntegrationRegistration(string detectedModVersion, IDigitalStorageRuntime runtime)
    {
        DetectedModVersion = detectedModVersion ?? string.Empty;
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <inheritdoc />
    public string DetectedModVersion { get; }

    /// <inheritdoc />
    public IDigitalStorageRuntime Runtime { get; }

    /// <summary>Gets the first reason the plugin cannot work, or null.</summary>
    internal string? Failure { get; private set; }

    internal IReadOnlyList<PatchRequest> Patches => _patches;

    internal IReadOnlyList<Func<string?>> Checks => _checks;

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
}
