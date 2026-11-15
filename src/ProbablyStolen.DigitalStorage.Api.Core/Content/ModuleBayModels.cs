using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Api.Content;

/// <summary>Immutable module-bay geometry where zero-pattern coordinates are available.</summary>
public sealed class ModuleBayLayout : IEquatable<ModuleBayLayout>
{
    /// <summary>Maximum width or height accepted for a layout.</summary>
    public const int MaximumDimension = 256;
    /// <summary>Maximum number of coordinates accepted in one layout.</summary>
    public const int MaximumArea = 65536;
    private readonly bool[,] _open;

    /// <summary>Creates a rectangular layout with every coordinate available.</summary>
    /// <param name="width">The positive horizontal dimension.</param>
    /// <param name="height">The positive vertical dimension.</param>
    public ModuleBayLayout(int width, int height)
        : this(width, height, CreateRectangleRows(width, height))
    {
    }

    /// <summary>Creates a rectangular layout with every coordinate available.</summary>
    /// <param name="width">The positive horizontal dimension.</param>
    /// <param name="height">The positive vertical dimension.</param>
    /// <returns>A rectangular layout.</returns>
    public static ModuleBayLayout Rectangle(int width, int height) => new(width, height);

    /// <summary>Creates a pattern layout and derives its dimensions from the rows.</summary>
    /// <param name="rows">Equal-width rows containing zero and one.</param>
    /// <returns>A pattern layout.</returns>
    public static ModuleBayLayout Pattern(IEnumerable<string> rows)
    {
        if (rows == null) throw new ArgumentNullException(nameof(rows));
        var values = new List<string>(Math.Min(16, MaximumDimension));
        var width = -1;
        foreach (var row in rows)
        {
            if (values.Count == MaximumDimension) throw new ArgumentException("A pattern exceeds the maximum height.", nameof(rows));
            if (row == null) throw new ArgumentException("Pattern rows cannot be null.", nameof(rows));
            if (width < 0) width = row.Length;
            if (row.Length != width || width <= 0 || width > MaximumDimension)
                throw new ArgumentException("Pattern rows must have a valid equal width.", nameof(rows));
            values.Add(row);
        }
        if (values.Count == 0) throw new ArgumentException("A pattern requires at least one row.", nameof(rows));
        return new ModuleBayLayout(width, values.Count, values);
    }

    /// <summary>Creates a rectangular layout from equal-width zero/one rows.</summary>
    /// <param name="width">The positive horizontal dimension.</param>
    /// <param name="height">The positive vertical dimension.</param>
    /// <param name="rows">Rows containing only zero (available) and one (blocked).</param>
    public ModuleBayLayout(int width, int height, IEnumerable<string> rows)
    {
        ValidateDimensions(width, height);
        Area = checked(width * height);
        if (rows == null) throw new ArgumentNullException(nameof(rows));
        var boundedRows = new List<string>();
        foreach (var row in rows)
        {
            if (boundedRows.Count == height) throw new ArgumentException("Layout has too many rows.", nameof(rows));
            boundedRows.Add(row);
        }
        var rowValues = boundedRows.ToArray();
        if (rowValues.Length != height || rowValues.Any(row => row == null || row.Length != width || row.Any(ch => ch != '0' && ch != '1')))
            throw new ArgumentException("Layout rows must match the dimensions and contain only zero or one.", nameof(rows));

        Width = width;
        Height = height;
        _open = new bool[width, height];
        var availableCells = 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                _open[x, y] = rowValues[y][x] == '0';
                if (_open[x, y]) availableCells = checked(availableCells + 1);
            }
        AvailableCells = availableCells;
        Rows = Array.AsReadOnly(rowValues);
    }

    private static IEnumerable<string> CreateRectangleRows(int width, int height)
    {
        ValidateDimensions(width, height);
        return Enumerable.Repeat(new string('0', width), height);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || width > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0 || height > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(height));
        if ((long)width * height > MaximumArea) throw new ArgumentOutOfRangeException(nameof(height), "Layout area exceeds the supported limit.");
    }

    /// <summary>Gets the horizontal dimension.</summary>
    public int Width { get; }

    /// <summary>Gets the vertical dimension.</summary>
    public int Height { get; }

    /// <summary>Gets the checked rectangle area.</summary>
    public int Area { get; }

    /// <summary>Gets the number of available coordinates.</summary>
    public int AvailableCells { get; }

    /// <summary>Gets an immutable copy of the row pattern.</summary>
    public IReadOnlyList<string> Rows { get; }

    /// <summary>Tests whether a coordinate is available; out-of-range coordinates are blocked.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns><see langword="true"/> when the coordinate is available.</returns>
    public bool IsOpen(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _open[x, y];

    /// <summary>Enumerates available coordinates in row-major order.</summary>
    public IEnumerable<(int X, int Y)> Coordinates()
    {
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (_open[x, y]) yield return (x, y);
    }

    /// <summary>Checks fixed-origin inclusion of another layout's available coordinates.</summary>
    /// <param name="previous">The layout whose available coordinates must remain open.</param>
    /// <returns><see langword="true"/> when every previous open coordinate remains open.</returns>
    public bool ContainsOpenCellsOf(ModuleBayLayout previous)
        => previous != null && previous.Coordinates().All(coordinate => IsOpen(coordinate.X, coordinate.Y));

    /// <inheritdoc />
    public bool Equals(ModuleBayLayout? other) => other != null && Width == other.Width && Height == other.Height
        && Rows.SequenceEqual(other.Rows, StringComparer.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ModuleBayLayout);

    /// <inheritdoc />
    public override int GetHashCode() => Rows.Aggregate((Width * 397) ^ Height, (hash, row) => unchecked(hash * 397 ^ row.GetHashCode()));
}

/// <summary>One immutable stage in a module-bay definition.</summary>
public sealed class ModuleBayStage
{
    /// <summary>Creates a module-bay stage.</summary>
    /// <param name="level">The zero-based stage level.</param>
    /// <param name="key">The unique stable stage key.</param>
    /// <param name="layout">The stage layout.</param>
    /// <param name="localizationKey">An optional localization identifier.</param>
    public ModuleBayStage(int level, string key, ModuleBayLayout layout, string? localizationKey = null)
    {
        if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
        if (!Identifier.TryParse("stage:" + key, out _)) throw new ArgumentException("Stage key must be a lowercase path identifier.", nameof(key));
        if (localizationKey != null && !Identifier.TryParse(localizationKey, out _))
            throw new ArgumentException("Localization key must be a lowercase identifier.", nameof(localizationKey));
        Level = level;
        Key = key;
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        LocalizationKey = localizationKey;
    }

    /// <summary>Gets the zero-based stage level.</summary>
    public int Level { get; }

    /// <summary>Gets the stable stage key.</summary>
    public string Key { get; }

    /// <summary>Gets the immutable stage layout.</summary>
    public ModuleBayLayout Layout { get; }

    /// <summary>Gets the optional localization identifier.</summary>
    public string? LocalizationKey { get; }
}

/// <summary>Immutable named module-bay definition with contiguous monotonic stages.</summary>
public sealed class ModuleBayDefinition
{
    /// <summary>Maximum number of stages in one definition.</summary>
    public const int MaximumStages = 64;
    /// <summary>Maximum summed layout area across all stages in one definition.</summary>
    public const int MaximumTotalArea = 262144;

    /// <summary>Creates a module-bay definition.</summary>
    /// <param name="id">The stable definition identifier.</param>
    /// <param name="stages">The stages, which are sorted by level when stored.</param>
    public ModuleBayDefinition(string id, IEnumerable<ModuleBayStage> stages)
    {
        if (!Identifier.TryParse(id, out _)) throw new ArgumentException("Definition id must be a lowercase namespace:path identifier.", nameof(id));
        var values = new List<ModuleBayStage>();
        var totalArea = 0;
        foreach (var stage in stages ?? throw new ArgumentNullException(nameof(stages)))
        {
            if (values.Count == MaximumStages) throw new ArgumentException("A definition exceeds the maximum stage count.", nameof(stages));
            if (stage == null) throw new ArgumentException("Stages cannot contain null values.", nameof(stages));
            totalArea = checked(totalArea + stage.Layout.Area);
            if (totalArea > MaximumTotalArea) throw new ArgumentException("Definition stage area exceeds the supported limit.", nameof(stages));
            values.Add(stage);
        }
        var stageValues = values.ToArray();
        Array.Sort(stageValues, (left, right) => left.Level.CompareTo(right.Level));
        if (stageValues.Length == 0 || stageValues.Select((stage, index) => stage.Level == index).Any(valid => !valid))
            throw new ArgumentException("Stages must be unique and contiguous from level zero.", nameof(stages));
        if (stageValues.Select(stage => stage.Key).Distinct(StringComparer.Ordinal).Count() != stageValues.Length)
            throw new ArgumentException("Stage keys must be unique.", nameof(stages));
        for (var index = 1; index < stageValues.Length; index++)
            if (!stageValues[index].Layout.ContainsOpenCellsOf(stageValues[index - 1].Layout))
                throw new ArgumentException("Each stage must preserve all previous open coordinates.", nameof(stages));
        Id = Identifier.Parse(id);
        Stages = Array.AsReadOnly(stageValues);
    }

    /// <summary>Gets the stable definition identifier.</summary>
    public Identifier Id { get; }

    /// <summary>Gets stages in ascending contiguous level order.</summary>
    public IReadOnlyList<ModuleBayStage> Stages { get; }

    /// <summary>Gets a stage by its zero-based level.</summary>
    /// <param name="level">The requested stage level.</param>
    /// <returns>The requested stage.</returns>
    public ModuleBayStage Stage(int level) => level >= 0 && level < Stages.Count ? Stages[level] : throw new ArgumentOutOfRangeException(nameof(level));
}

/// <summary>Maps a native item identity to a definition, provider, and optional stage limit.</summary>
public sealed class ModuleBayBinding
{
    /// <summary>Creates a module-bay binding.</summary>
    /// <param name="nativeItemId">The stable native item identifier.</param>
    /// <param name="definitionId">The referenced definition identifier.</param>
    /// <param name="providerId">The stable provider identifier.</param>
    /// <param name="maximumStage">An optional maximum supported stage.</param>
    public ModuleBayBinding(string nativeItemId, string definitionId, string providerId, int? maximumStage = null)
    {
        if (string.IsNullOrWhiteSpace(nativeItemId) || !string.Equals(nativeItemId, nativeItemId.Trim(), StringComparison.Ordinal)
            || nativeItemId.Any(char.IsControl)) throw new ArgumentException("Native item id must be a stable non-empty value.", nameof(nativeItemId));
        if (nativeItemId == "*") throw new ArgumentException("Wildcard bindings are not supported; bind each native item id exactly.", nameof(nativeItemId));
        if (!Identifier.TryParse(definitionId, out var parsedDefinition)) throw new ArgumentException("Definition id must be a valid identifier.", nameof(definitionId));
        if (!Identifier.TryParse(providerId, out var parsedProvider)) throw new ArgumentException("Provider id must be a valid identifier.", nameof(providerId));
        if (maximumStage < 0) throw new ArgumentOutOfRangeException(nameof(maximumStage));
        NativeItemId = nativeItemId;
        DefinitionId = parsedDefinition;
        ProviderId = parsedProvider;
        MaximumStage = maximumStage;
    }

    /// <summary>Gets the stable native item identifier.</summary>
    public string NativeItemId { get; }

    /// <summary>Gets the referenced definition identifier.</summary>
    public Identifier DefinitionId { get; }

    /// <summary>Gets the stable provider identifier.</summary>
    public Identifier ProviderId { get; }

    /// <summary>Gets the optional maximum stage.</summary>
    public int? MaximumStage { get; }
}

/// <summary>Describes the outcome category of a module-bay change.</summary>
public enum ModuleBayChangeStatus
{
    /// <summary>The requested change completed.</summary>
    Changed,
    /// <summary>The operation was refused without applying the change.</summary>
    Refused,
    /// <summary>The target or provider is unavailable.</summary>
    Unavailable,
    /// <summary>The requested state is invalid.</summary>
    Invalid,
    /// <summary>Material consumption outcome could not be determined.</summary>
    ConsumptionUncertain
}

/// <summary>Immutable result of a module-bay stage attempt.</summary>
public sealed class ModuleBayChangeResult
{
    private ModuleBayChangeResult(ModuleBayChangeStatus status, int stage, string? reason)
    { Status = status; Stage = stage; Reason = reason; }

    /// <summary>Gets the result category.</summary>
    public ModuleBayChangeStatus Status { get; }

    /// <summary>Gets the resulting or observed stage.</summary>
    public int Stage { get; }

    /// <summary>Gets the optional diagnostic reason.</summary>
    public string? Reason { get; }

    /// <summary>Gets whether the stage change succeeded.</summary>
    public bool Succeeded => Status == ModuleBayChangeStatus.Changed;

    /// <summary>Creates a successful result.</summary>
    /// <param name="stage">The resulting stage.</param>
    /// <returns>A successful result.</returns>
    public static ModuleBayChangeResult Changed(int stage)
    {
        if (stage < 0) throw new ArgumentOutOfRangeException(nameof(stage));
        return new ModuleBayChangeResult(ModuleBayChangeStatus.Changed, stage, null);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="stage">The observed stage.</param>
    /// <param name="reason">The refusal reason.</param>
    /// <returns>A refusal result.</returns>
    public static ModuleBayChangeResult Refused(int stage, string reason) => Failed(ModuleBayChangeStatus.Refused, stage, reason);

    /// <summary>Creates a categorized failure result.</summary>
    /// <param name="status">The failure category, which cannot be <see cref="ModuleBayChangeStatus.Changed"/>.</param>
    /// <param name="stage">The observed stage.</param>
    /// <param name="reason">The failure reason.</param>
    /// <returns>A failure result.</returns>
    public static ModuleBayChangeResult Failed(ModuleBayChangeStatus status, int stage, string reason)
    {
        if (status == ModuleBayChangeStatus.Changed) throw new ArgumentException("Use Changed to create a success result.", nameof(status));
        if (stage < 0) throw new ArgumentOutOfRangeException(nameof(stage));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A failure reason is required.", nameof(reason));
        return new ModuleBayChangeResult(status, stage, reason);
    }
}
