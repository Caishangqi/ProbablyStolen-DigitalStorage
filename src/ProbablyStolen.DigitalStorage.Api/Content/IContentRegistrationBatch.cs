using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Api;

/// <summary>
/// A temporary batch used only during a contributor callback. Entries go to the builtin registries named by
/// <c>DigitalStorageRegistries</c>, or to registries a batch declares, through
/// <c>DeferredRegister&lt;T&gt;.Create(batch.Registries, key, modId)</c>.
/// </summary>
public interface IContentRegistrationBatch
{
    /// <summary>Provides scoped declarations and contributions to shared registries.</summary>
    RegistryRegistration Registries { get; }
}
