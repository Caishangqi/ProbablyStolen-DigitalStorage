using System;
using ProbablyStolen.DigitalStorage.Compat;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Mod.Entry;

/// <summary>Bridges the game loader lifecycle to the platform composition.</summary>
public sealed class DigitalStorageMod : IMod
{
    private PlatformComposition? _composition;

    /// <summary>Creates the startup composition from the loader manifest.</summary>
    /// <param name="manifest">The manifest supplied by the game loader.</param>
    /// <exception cref="ArgumentNullException">The manifest is null.</exception>
    public void Init(ModManifest manifest)
    {
        if (_composition is not null)
        {
            return;
        }

        _composition = PlatformComposition.Create(manifest);
        try
        {
            // Literal typeof(...).Assembly.GetTypes(): the mod scanner reads it as reflection on this mod's own types.
            _composition.AttachIntegrations(typeof(CompatAssembly).Assembly.GetTypes());
        }
        catch (Exception error)
        {
            // Integrations are optional; Digital Storage itself keeps working without them.
            UnityEngine.Debug.LogWarning($"Digital Storage: mod integrations are unavailable ({error.GetType().Name}: {error.Message}).");
        }
    }

    /// <summary>Enables the owned startup composition.</summary>
    /// <exception cref="InvalidOperationException">Initialization has not completed.</exception>
    public void OnEnable()
    {
        var composition = _composition ?? throw new InvalidOperationException("The mod has not been initialized.");
        composition.Enable();
    }

    /// <summary>Disposes the owned startup composition.</summary>
    public void OnDisable()
    {
        var composition = _composition;
        _composition = null;
        composition?.Dispose();
    }
}
