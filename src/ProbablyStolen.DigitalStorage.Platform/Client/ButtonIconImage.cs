using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal static class ButtonIconImage
{
    internal const int Size = 16;

    internal static bool HasVisiblePixels(int width, int height, IReadOnlyList<byte> alpha)
    {
        if (width != Size || height != Size || alpha.Count != Size * Size) return false;
        for (var index = 0; index < alpha.Count; index++)
            if (alpha[index] != 0) return true;
        return false;
    }
}
