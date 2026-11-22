namespace ProbablyStolen.DigitalStorage.Compat.ClientDemandHighlight;

internal enum DemandKind
{
    None,
    Buying,
    Inspection
}

internal static class DemandClassifier
{
    internal static int? Classify(DemandKind kind, bool buying, bool wanted, bool contraband, bool stolen,
        bool showNormal, bool showStolen, bool showContraband)
    {
        if (kind == DemandKind.None) return null;
        if (kind == DemandKind.Buying && (!buying || !wanted)) return null;
        if (contraband) return showContraband ? 0xF23833 : null;
        if (stolen) return showStolen ? 0xFFB31A : null;
        return kind == DemandKind.Buying && showNormal ? 0x4DE659 : null;
    }
}
