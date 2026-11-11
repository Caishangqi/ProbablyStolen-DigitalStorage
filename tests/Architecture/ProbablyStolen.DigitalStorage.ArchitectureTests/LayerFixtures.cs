using System;

// Fixture types that reach another layer only through one kind of reference each. CommonLayerTests reads this test
// assembly and must find every edge; a scanner that missed one kind would let that kind of violation through.

namespace ProbablyStolen.DigitalStorage.ArchitectureTests.LayerFixture.Application.Calls
{
    internal static class Target
    {
        internal static void Run()
        {
        }
    }
}

namespace ProbablyStolen.DigitalStorage.ArchitectureTests.LayerFixture.Application.Catches
{
    internal sealed class TargetException : Exception
    {
    }
}

namespace ProbablyStolen.DigitalStorage.ArchitectureTests.LayerFixture.Application.Attributes
{
    [AttributeUsage(AttributeTargets.All)]
    internal sealed class TargetAttribute : Attribute
    {
    }
}

namespace ProbablyStolen.DigitalStorage.ArchitectureTests.LayerFixture.Domain.ByCall
{
    internal static class Source
    {
        // No signature, local or field names the target type; only the call token in the body does.
        internal static void Use() => Application.Calls.Target.Run();
    }
}

namespace ProbablyStolen.DigitalStorage.ArchitectureTests.LayerFixture.Domain.ByCatch
{
    internal static class Source
    {
        internal static bool Use(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Application.Catches.TargetException)
            {
                return false;
            }
        }
    }
}

namespace ProbablyStolen.DigitalStorage.ArchitectureTests.LayerFixture.Domain.ByAttribute
{
    [Application.Attributes.Target]
    internal sealed class Source
    {
    }
}
