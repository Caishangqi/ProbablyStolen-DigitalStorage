using System.Runtime.CompilerServices;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

internal sealed class CellContentStamps<TCell> where TCell : class
{
    private readonly ConditionalWeakTable<TCell, object> _stamps = new();

    internal object Read(TCell cell) => _stamps.GetValue(cell, _ => new object());

    internal void Invalidate(TCell cell) => _stamps.Remove(cell);
}
