using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

internal static class ItemDirectory
{
    internal static GameItem CreateEmptyItem(string id) => throw new InvalidOperationException("unexpected item creation");
}

namespace ProbablyStolen.DigitalStorage.Platform.Content
{
    internal sealed class NativeRandom : IRandom
    {
        internal static readonly NativeRandom Instance = new();
        public int Next(int minInclusive, int maxExclusive) => throw new InvalidOperationException("unexpected native random");
    }

    internal sealed class ItemAppearance
    {
        internal string? Sprite { get; private set; }
        internal void SetPreparedSprite(GameItem item, string spriteKey) => Sprite = spriteKey;
        internal void EnsureReady(string spriteKey) => throw new InvalidOperationException("unexpected sprite load");
        internal void Apply(GameItem item, string spriteKey, int width, int height) =>
            throw new InvalidOperationException("unexpected item appearance setup");
    }
}
