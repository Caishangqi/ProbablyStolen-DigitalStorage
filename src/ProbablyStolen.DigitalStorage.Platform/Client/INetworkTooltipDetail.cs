using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal interface INetworkTooltipDetail
{
    Identifier Identity { get; }
    void Append(NativeTooltipContext context, Grid grid);
}
