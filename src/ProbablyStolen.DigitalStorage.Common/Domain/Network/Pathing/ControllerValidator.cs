using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;

/// <summary>Validates the supported controller structure without inspecting power or device roles.</summary>
public static class ControllerValidator
{
    /// <summary>Classifies the controller set. Controller multiblocks are outside this implementation.</summary>
    public static ControllerState CalculateState(IReadOnlyList<GridNode> nodes)
    {
        if (nodes == null)
        {
            throw new ArgumentNullException(nameof(nodes));
        }

        var controllers = 0;
        foreach (var node in nodes)
        {
            if (node.Kind == GridNodeKind.Controller)
            {
                controllers++;
            }
        }

        return controllers switch
        {
            0 => ControllerState.NoController,
            1 => ControllerState.Online,
            _ => ControllerState.ControllerConflict
        };
    }
}
