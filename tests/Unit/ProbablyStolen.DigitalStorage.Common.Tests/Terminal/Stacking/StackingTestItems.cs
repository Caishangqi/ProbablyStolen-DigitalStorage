using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Common.Tests.Terminal.Stacking;

/// <summary>Builds canonical keys shaped like the game items the stacking rules talk about.</summary>
internal static class StackingTestItems
{
    internal const string IngotId = "metal_ingot";
    internal const string BottleId = "water_tablet_container";

    internal static ItemKey Ingot(string purity, bool discovered = true, long unitValue = 50, int purityMod = 0,
        IEnumerable<CanonicalIdentityFeature>? extra = null)
    {
        var features = new List<CanonicalIdentityFeature>
        {
            new("INGOT_PURITY_TAG", "purity", valueModifier: purityMod, publicDisplay: "Metal Purity: " + purity,
                isFeatureExposed: true, isFeatureDiscovered: discovered)
        };
        if (extra != null) features.AddRange(extra);
        return CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(IngotId, new[] { "MATERIAL" },
            new[]
            {
                new CanonicalIdentityTag("INGOT_PURITY_FAKE_TAG", enabled: true, valueString: purity),
                new CanonicalIdentityTag("INGOT_PURITY_TAG", enabled: true, valueString: purity)
            },
            features, name: "Metal Ingot", unitValue: unitValue));
    }

    internal static ItemKey Bottle(int tablets)
    {
        var children = Enumerable.Range(0, tablets).Select(index => new CanonicalIdentityChild(
            new CanonicalIdentityInput("water_tablet", Array.Empty<string>(), Array.Empty<CanonicalIdentityTag>(),
                Array.Empty<CanonicalIdentityFeature>(), unitValue: 6),
            1, Shape(index), Shape(index))).ToArray();
        var grid = CanonicalIdentityNode.Grid(new CanonicalIdentityShape(0, 0, 1, 9, false, 0, 0, new byte[9]), children);
        return CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(BottleId, Array.Empty<string>(),
            Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>(), unitValue: 1,
            contentWindow: CanonicalIdentityNode.Window(new[] { grid })));
    }

    internal static ItemKey Plain(string id, long unitValue = 10, IEnumerable<CanonicalIdentityTag>? tags = null,
        IEnumerable<CanonicalIdentityFeature>? features = null) =>
        CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(id, Array.Empty<string>(),
            (tags ?? Array.Empty<CanonicalIdentityTag>()).ToArray(),
            (features ?? Array.Empty<CanonicalIdentityFeature>()).ToArray(), unitValue: unitValue));

    internal static StackingConfig Ignore(params string[] fields) =>
        new(true, fields.Select(field => new StackingRule(field, StackingMode.Ignore)));

    internal static StackingAttributes Value(long appraised) =>
        new(new Dictionary<string, long> { [StackingFieldCatalog.AppraisedValue] = appraised });

    private static CanonicalIdentityShape Shape(int y) => new(0, y, 1, 1, false, 0, 0, new byte[] { 0 });
}
