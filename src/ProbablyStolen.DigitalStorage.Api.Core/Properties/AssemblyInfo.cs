using System.Runtime.CompilerServices;

// The canonical identity encoding is an implementation detail of ItemKey; only the product assemblies that produce
// and read canonical state, and their tests, may use it.
[assembly: InternalsVisibleTo("ProbablyStolen.DigitalStorage.Common")]
[assembly: InternalsVisibleTo("ProbablyStolen.DigitalStorage.Platform")]
[assembly: InternalsVisibleTo("ProbablyStolen.DigitalStorage.Common.Tests")]
[assembly: InternalsVisibleTo("ProbablyStolen.DigitalStorage.Platform.Tests")]
