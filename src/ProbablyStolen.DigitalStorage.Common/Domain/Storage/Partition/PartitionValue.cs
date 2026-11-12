using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>The kind of a partition value.</summary>
public enum PartitionValueKind
{
    /// <summary>A stable text value: an item identifier, a type or an enum value.</summary>
    Text = 0,

    /// <summary>A whole number.</summary>
    Number = 1
}

/// <summary>The target of a partition condition. Booleans carry no target; the operator says "is" or "is not".</summary>
public sealed class PartitionValue : IEquatable<PartitionValue>
{
    private PartitionValue(PartitionValueKind kind, string? text, long number)
    {
        Kind = kind;
        Text = text;
        Number = number;
    }

    /// <summary>Gets the kind.</summary>
    public PartitionValueKind Kind { get; }

    /// <summary>Gets the text of a text value.</summary>
    public string? Text { get; }

    /// <summary>Gets the number of a number value.</summary>
    public long Number { get; }

    /// <summary>Creates a text value; empty text is not a value.</summary>
    public static PartitionValue Of(string text) =>
        string.IsNullOrEmpty(text) ? throw new ArgumentException("A text value is not empty.", nameof(text))
            : new PartitionValue(PartitionValueKind.Text, text, 0);

    /// <summary>Creates a number value.</summary>
    public static PartitionValue Of(long number) => new(PartitionValueKind.Number, null, number);

    /// <inheritdoc />
    public bool Equals(PartitionValue? other) =>
        other != null && Kind == other.Kind && string.Equals(Text, other.Text, StringComparison.Ordinal) && Number == other.Number;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PartitionValue);

    /// <inheritdoc />
    public override int GetHashCode() => Kind == PartitionValueKind.Text
        ? StringComparer.Ordinal.GetHashCode(Text!)
        : Number.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => Kind == PartitionValueKind.Text ? Text! : Number.ToString(CultureInfo.InvariantCulture);
}

/// <summary>What the player knows about one attribute of an item.</summary>
public sealed class AttributeValue
{
    private static readonly IReadOnlyCollection<string> NoTexts = Array.Empty<string>();

    private AttributeValue(bool isKnown, string? text, bool flag, long number, IReadOnlyCollection<string>? texts)
    {
        IsKnown = isKnown;
        Text = text;
        Flag = flag;
        Number = number;
        Texts = texts ?? NoTexts;
    }

    /// <summary>Gets an attribute the player cannot see, or that could not be read.</summary>
    public static AttributeValue Unknown { get; } = new(false, null, false, 0, null);

    /// <summary>Gets whether the value is known.</summary>
    public bool IsKnown { get; }

    /// <summary>Gets a known text value.</summary>
    public string? Text { get; }

    /// <summary>Gets a known boolean value.</summary>
    public bool Flag { get; }

    /// <summary>Gets a known number value.</summary>
    public long Number { get; }

    /// <summary>Gets a known set of texts, such as item types.</summary>
    public IReadOnlyCollection<string> Texts { get; }

    /// <summary>Creates a known text value.</summary>
    public static AttributeValue Of(string text) =>
        new(true, text ?? throw new ArgumentNullException(nameof(text)), false, 0, null);

    /// <summary>Creates a known boolean value.</summary>
    public static AttributeValue Of(bool flag) => new(true, null, flag, 0, null);

    /// <summary>Creates a known number value.</summary>
    public static AttributeValue Of(long number) => new(true, null, false, number, null);

    /// <summary>Creates a known set of texts; an empty set is known to contain nothing.</summary>
    public static AttributeValue OfSet(IEnumerable<string> texts)
    {
        if (texts == null) throw new ArgumentNullException(nameof(texts));
        var set = new HashSet<string>(texts.Where(text => !string.IsNullOrEmpty(text)), StringComparer.Ordinal);
        return new AttributeValue(true, null, false, 0, set);
    }
}
