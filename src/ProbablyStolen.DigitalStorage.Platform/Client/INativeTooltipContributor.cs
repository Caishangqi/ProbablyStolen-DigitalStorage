using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal interface INativeTooltipContributor
{
    Identifier Identity { get; }
    void Append(NativeTooltipContext context);
}
