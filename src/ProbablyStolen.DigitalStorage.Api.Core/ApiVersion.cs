namespace ProbablyStolen.DigitalStorage.Api;

/// <summary>
/// Version of the public Digital Storage API (Api.Core and Api). Breaking changes only come with a new major version.
/// </summary>
public static class ApiVersion
{
    /// <summary>Gets the major version; it changes when a public member is removed or changes meaning.</summary>
    public const int Major = 1;

    /// <summary>Gets the minor version; it changes when public members are added.</summary>
    public const int Minor = 0;
}
