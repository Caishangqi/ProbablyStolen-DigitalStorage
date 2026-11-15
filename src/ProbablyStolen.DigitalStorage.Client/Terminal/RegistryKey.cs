using System.Globalization;
using System.Text;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Client.Terminal;

internal static class RegistryKey
{
    internal static Identifier For(string value)
    {
        var path = new StringBuilder("id");
        foreach (var character in value) path.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
        return new Identifier("digital_storage", path.ToString());
    }
}
