using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Compat;
using ProbablyStolen.DigitalStorage.Mod.Patches;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Mod.Entry;

/// <summary>Bridges the game loader lifecycle to the platform composition.</summary>
public sealed class DigitalStorageMod : IMod
{
    private PlatformComposition? _composition;

    /// <summary>Applies the game patches the running build supports, then creates the startup composition.</summary>
    /// <param name="manifest">The manifest supplied by the game loader.</param>
    /// <exception cref="ArgumentNullException">The manifest is null.</exception>
    /// <exception cref="GameIncompatibleException">The running build lacks a member that keeps stored items intact.</exception>
    public void Init(ModManifest manifest)
    {
        if (_composition is not null)
        {
            return;
        }

        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        InstallPatches(manifest.ID);
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
        ModConfigWindowPatch.Attach(composition.GroupLogSettings);
    }

    /// <summary>Disposes the owned startup composition.</summary>
    public void OnDisable()
    {
        var composition = _composition;
        _composition = null;
        ModConfigWindowPatch.Detach();
        composition?.Dispose();
        GamePatchStatus.Publish(Array.Empty<GameFeature>());
    }

    // Patches are applied group by group so that a member missing from the running build only turns its feature off;
    // only a missing member that keeps stored items intact stops the mod. Any other failure is logged with the build
    // before the loader stops the mod. These startup lines bypass the log categories on purpose: they run before the
    // settings exist, and every player report needs them.
    private static void InstallPatches(string modId)
    {
        var build = $"game {UnityEngine.Application.version}";
        try
        {
            var results = GamePatchInstaller.Install(GamePatchGroups.Create(), new HarmonyGamePatchApplier(modId));
            var applied = results.Count(result => result.State == GamePatchState.Applied);
            UnityEngine.Debug.Log($"Digital Storage: {build}; {applied}/{results.Count} patch groups applied.");
            foreach (var result in results.Where(result => result.State != GamePatchState.Applied))
                UnityEngine.Debug.LogWarning($"Digital Storage: {result.Describe()}; this feature is off on this game build.");
        }
        catch (GameIncompatibleException error)
        {
            UnityEngine.Debug.LogError($"Digital Storage: {build}. {error.Message}");
            throw;
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogError($"Digital Storage: {build}; the game patches could not be prepared " +
                $"({error.GetType().Name}: {error.Message}). Do not save games that contain Digital Storage items.");
            throw;
        }
    }
}
