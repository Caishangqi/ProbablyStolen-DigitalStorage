using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Items;

/// <summary>Detached values used only to create an identity, never a physical item.</summary>
public sealed class CanonicalIdentityInput
{
    /// <summary>Creates an immutable item identity projection.</summary>
    public CanonicalIdentityInput(string identifier, IReadOnlyList<string> itemTypes,
        IReadOnlyList<CanonicalIdentityTag> tags, IReadOnlyList<CanonicalIdentityFeature> features,
        string? name = null, string? shortDescription = null,
        string? flavorText = null, long unitBaseValue = 0, long unitValue = 0, long lateUnitValue = 0,
        long backupUnitValue = 0, string? spriteAtlasPath = null,
        string? spritePath = null, CanonicalIdentityNode? contentWindow = null,
        CanonicalIdentityNode? examineWindow = null)
    {
        Identifier = identifier;
        Name = name;
        ShortDescription = shortDescription;
        FlavorText = flavorText;
        UnitBaseValue = unitBaseValue;
        UnitValue = unitValue;
        LateUnitValue = lateUnitValue;
        BackupUnitValue = backupUnitValue;
        ItemTypes = Copy(itemTypes ?? throw new ArgumentNullException(nameof(itemTypes)));
        SpriteAtlasPath = spriteAtlasPath;
        SpritePath = spritePath;
        Tags = Copy(tags ?? throw new ArgumentNullException(nameof(tags)), 65536);
        Features = Copy(features ?? throw new ArgumentNullException(nameof(features)), 65536);
        ContentWindow = contentWindow;
        ExamineWindow = examineWindow;
    }

    /// <summary>Gets the root or child definition identifier.</summary>
    public string Identifier { get; }
    /// <summary>Gets the saved name.</summary>
    public string? Name { get; }
    /// <summary>Gets the saved short description.</summary>
    public string? ShortDescription { get; }
    /// <summary>Gets the saved flavor text.</summary>
    public string? FlavorText { get; }
    /// <summary>Gets the saved base value.</summary>
    public long UnitBaseValue { get; }
    /// <summary>Gets the saved unit value.</summary>
    public long UnitValue { get; }
    /// <summary>Gets the saved late value.</summary>
    public long LateUnitValue { get; }
    /// <summary>Gets the saved backup value.</summary>
    public long BackupUnitValue { get; }
    /// <summary>Gets the ordered item types.</summary>
    public IReadOnlyList<string> ItemTypes { get; }
    /// <summary>Gets the saved atlas path.</summary>
    public string? SpriteAtlasPath { get; }
    /// <summary>Gets the saved sprite path.</summary>
    public string? SpritePath { get; }
    /// <summary>Gets detached tag values.</summary>
    public IReadOnlyList<CanonicalIdentityTag> Tags { get; }
    /// <summary>Gets detached feature values.</summary>
    public IReadOnlyList<CanonicalIdentityFeature> Features { get; }
    /// <summary>Gets the content graph.</summary>
    public CanonicalIdentityNode? ContentWindow { get; }
    /// <summary>Gets the examine graph.</summary>
    public CanonicalIdentityNode? ExamineWindow { get; }

    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T>? values, int maxCount = 1048576)
    {
        if (values == null) return Array.Empty<T>();
        var count = values.Count;
        if (count < 0 || count > maxCount) throw new ArgumentException("The input collection is too large.", nameof(values));
        var copy = new T[count];
        for (var i = 0; i < count; i++) copy[i] = values[i];
        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>Detached tag fields including raw IEEE value bits.</summary>
public sealed class CanonicalIdentityTag
{
    /// <summary>Creates tag fields without applying native tag helpers.</summary>
    public CanonicalIdentityTag(string key, string? identifier = null, string? identifierName = null,
        bool enabled = false, string? valueString = null, int valueInt = 0, uint valueFloatBits = 0,
        long valueLong = 0, ulong valueDoubleBits = 0, bool valueBool = false)
    {
        Key = key; Identifier = identifier; IdentifierName = identifierName; Enabled = enabled;
        ValueString = valueString; ValueInt = valueInt; ValueFloatBits = valueFloatBits;
        ValueLong = valueLong; ValueDoubleBits = valueDoubleBits; ValueBool = valueBool;
    }
    /// <summary>Gets the dictionary key.</summary>
    public string Key { get; }
    /// <summary>Gets the stored tag identifier.</summary>
    public string? Identifier { get; }
    /// <summary>Gets the stored identifier name.</summary>
    public string? IdentifierName { get; }
    /// <summary>Gets the enabled flag.</summary>
    public bool Enabled { get; }
    /// <summary>Gets the string value.</summary>
    public string? ValueString { get; }
    /// <summary>Gets the integer value.</summary>
    public int ValueInt { get; }
    /// <summary>Gets the exact float bits.</summary>
    public uint ValueFloatBits { get; }
    /// <summary>Gets the long value.</summary>
    public long ValueLong { get; }
    /// <summary>Gets the exact double bits.</summary>
    public ulong ValueDoubleBits { get; }
    /// <summary>Gets the boolean value.</summary>
    public bool ValueBool { get; }
}

/// <summary>Detached condition fields.</summary>
public sealed class CanonicalIdentityCondition
{
    /// <summary>Creates a condition with an explicit action identifier.</summary>
    public CanonicalIdentityCondition(string? display = null, string? identifier = null, int modValue = 0,
        int modValueDisplay = 0, bool isTransformative = false, string? category = null,
        bool hiddenAsPublic = false, int customValue = 0, string? onActivateActionId = null, int compareValue = 0)
    {
        Display = display; Identifier = identifier; ModValue = modValue; ModValueDisplay = modValueDisplay;
        IsTransformative = isTransformative; Category = category; HiddenAsPublic = hiddenAsPublic;
        CustomValue = customValue; OnActivateActionId = onActivateActionId; CompareValue = compareValue;
    }
    /// <summary>Gets the display text.</summary>
    public string? Display { get; }
    /// <summary>Gets the identifier.</summary>
    public string? Identifier { get; }
    /// <summary>Gets the value modifier.</summary>
    public int ModValue { get; }
    /// <summary>Gets the displayed value modifier.</summary>
    public int ModValueDisplay { get; }
    /// <summary>Gets whether the condition transforms the item.</summary>
    public bool IsTransformative { get; }
    /// <summary>Gets the category.</summary>
    public string? Category { get; }
    /// <summary>Gets the public visibility flag.</summary>
    public bool HiddenAsPublic { get; }
    /// <summary>Gets the custom value.</summary>
    public int CustomValue { get; }
    /// <summary>Gets the activation action identifier (for example "counterfeitInjector"), or null.</summary>
    public string? OnActivateActionId { get; }
    /// <summary>Gets the comparison value.</summary>
    public int CompareValue { get; }
}

/// <summary>Detached feature fields; ownership is the containing input item.</summary>
public sealed class CanonicalIdentityFeature
{
    /// <summary>Creates an immutable feature projection.</summary>
    public CanonicalIdentityFeature(string identifier, string category, int featureType = 0, int valueStage = 0,
        string? publicDisplay = null, string? actualDisplay = null, string? removedByTool = null,
        int valueModifier = 0, bool initiallyShown = false, bool isFeatureExposed = false,
        bool isExposing = false, bool isFeatureDiscovered = false, CanonicalIdentityCondition? fakeCondition = null,
        CanonicalIdentityCondition? realCondition = null, bool useCondition = false, bool usePreExposeValue = false,
        int preExposeValueModifier = 0, bool isExposable = false, bool isPublicHidden = false,
        int customIntValue1 = 0, bool isDisabled = false, bool isFeatureMatch = false)
    {
        Identifier = identifier; Category = category; FeatureType = featureType; ValueStage = valueStage;
        PublicDisplay = publicDisplay; ActualDisplay = actualDisplay; RemovedByTool = removedByTool;
        ValueModifier = valueModifier; InitiallyShown = initiallyShown; IsFeatureExposed = isFeatureExposed;
        IsExposing = isExposing; IsFeatureDiscovered = isFeatureDiscovered; FakeCondition = fakeCondition;
        RealCondition = realCondition; UseCondition = useCondition; UsePreExposeValue = usePreExposeValue;
        PreExposeValueModifier = preExposeValueModifier; IsExposable = isExposable; IsPublicHidden = isPublicHidden;
        CustomIntValue1 = customIntValue1; IsDisabled = isDisabled; IsFeatureMatch = isFeatureMatch;
    }
    /// <summary>Gets the feature identifier.</summary>
    public string Identifier { get; }
    /// <summary>Gets the feature category.</summary>
    public string Category { get; }
    /// <summary>Gets the source feature type value.</summary>
    public int FeatureType { get; }
    /// <summary>Gets the source value stage.</summary>
    public int ValueStage { get; }
    /// <summary>Gets the public display.</summary>
    public string? PublicDisplay { get; }
    /// <summary>Gets the actual display.</summary>
    public string? ActualDisplay { get; }
    /// <summary>Gets the removal tool identifier.</summary>
    public string? RemovedByTool { get; }
    /// <summary>Gets the value modifier.</summary>
    public int ValueModifier { get; }
    /// <summary>Gets the initial display flag.</summary>
    public bool InitiallyShown { get; }
    /// <summary>Gets the exposure flag.</summary>
    public bool IsFeatureExposed { get; }
    /// <summary>Gets the exposing flag.</summary>
    public bool IsExposing { get; }
    /// <summary>Gets the discovery flag.</summary>
    public bool IsFeatureDiscovered { get; }
    /// <summary>Gets the optional fake condition.</summary>
    public CanonicalIdentityCondition? FakeCondition { get; }
    /// <summary>Gets the optional real condition.</summary>
    public CanonicalIdentityCondition? RealCondition { get; }
    /// <summary>Gets whether conditions are used.</summary>
    public bool UseCondition { get; }
    /// <summary>Gets whether the value before exposure is used.</summary>
    public bool UsePreExposeValue { get; }
    /// <summary>Gets the modifier before exposure.</summary>
    public int PreExposeValueModifier { get; }
    /// <summary>Gets the exposable flag.</summary>
    public bool IsExposable { get; }
    /// <summary>Gets whether the feature is hidden publicly.</summary>
    public bool IsPublicHidden { get; }
    /// <summary>Gets the custom value.</summary>
    public int CustomIntValue1 { get; }
    /// <summary>Gets the disabled flag.</summary>
    public bool IsDisabled { get; }
    /// <summary>Gets the feature match flag.</summary>
    public bool IsFeatureMatch { get; }
}

/// <summary>Detached shape geometry and mask.</summary>
public sealed class CanonicalIdentityShape
{
    private readonly byte[] _mask;
    /// <summary>Creates a detached shape value.</summary>
    public CanonicalIdentityShape(int minX, int minY, int width, int height, bool flipped, int orientation,
        byte outside, byte[] mask)
    {
        if (mask == null) throw new ArgumentNullException(nameof(mask));
        if (mask.Length > 1048576) throw new ArgumentException("The mask is too large.", nameof(mask));
        MinX = minX; MinY = minY; Width = width; Height = height; Flipped = flipped;
        Orientation = orientation; Outside = outside; _mask = (byte[])mask.Clone();
    }
    /// <summary>Gets the X origin.</summary>
    public int MinX { get; }
    /// <summary>Gets the Y origin.</summary>
    public int MinY { get; }
    /// <summary>Gets the local width.</summary>
    public int Width { get; }
    /// <summary>Gets the local height.</summary>
    public int Height { get; }
    /// <summary>Gets the flip flag.</summary>
    public bool Flipped { get; }
    /// <summary>Gets the rotation.</summary>
    public int Orientation { get; }
    /// <summary>Gets the outside byte.</summary>
    public byte Outside { get; }
    /// <summary>Gets a detached copy of the mask.</summary>
    public byte[] CopyMask() => (byte[])_mask.Clone();
}

/// <summary>Detached child identity and placement.</summary>
public sealed class CanonicalIdentityChild
{
    /// <summary>Creates child placement facts.</summary>
    public CanonicalIdentityChild(CanonicalIdentityInput item, int unitCount, CanonicalIdentityShape originalShape,
        CanonicalIdentityShape modifiedShape)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item)); UnitCount = unitCount;
        OriginalShape = originalShape ?? throw new ArgumentNullException(nameof(originalShape));
        ModifiedShape = modifiedShape ?? throw new ArgumentNullException(nameof(modifiedShape));
    }
    /// <summary>Gets the child identity projection.</summary>
    public CanonicalIdentityInput Item { get; }
    /// <summary>Gets child quantity.</summary>
    public int UnitCount { get; }
    /// <summary>Gets the original shape.</summary>
    public CanonicalIdentityShape OriginalShape { get; }
    /// <summary>Gets the modified shape.</summary>
    public CanonicalIdentityShape ModifiedShape { get; }
}

/// <summary>Detached version 1 graph node.</summary>
public sealed class CanonicalIdentityNode
{
    private CanonicalIdentityNode(byte kind, IReadOnlyList<CanonicalIdentityNode>? nodes = null,
        IReadOnlyList<CanonicalIdentityChild>? items = null, CanonicalIdentityShape? shape = null,
        int width = 0, int height = 0, bool alwaysStack = false, int orientation = 0,
        CanonicalIdentityChild? slotItem = null)
    {
        Kind = kind; Nodes = CanonicalIdentityInput.Copy(nodes); Items = CanonicalIdentityInput.Copy(items);
        Shape = shape; Width = width; Height = height; AlwaysStack = alwaysStack;
        Orientation = orientation; SlotItem = slotItem;
    }
    /// <summary>Creates an ordered window node.</summary>
    public static CanonicalIdentityNode Window(IReadOnlyList<CanonicalIdentityNode> children) => new(1, nodes: children ?? throw new ArgumentNullException(nameof(children)));
    /// <summary>Creates a fixed grid node.</summary>
    public static CanonicalIdentityNode Grid(CanonicalIdentityShape shape, IReadOnlyList<CanonicalIdentityChild> items) =>
        new(2, items: items ?? throw new ArgumentNullException(nameof(items)), shape: shape ?? throw new ArgumentNullException(nameof(shape)));
    /// <summary>Creates a scrollable grid node.</summary>
    public static CanonicalIdentityNode Scrollable(int width, int height, bool alwaysStack, IReadOnlyList<CanonicalIdentityChild> items) =>
        new(3, items: items ?? throw new ArgumentNullException(nameof(items)), width: width, height: height, alwaysStack: alwaysStack);
    /// <summary>Creates a slot node with optional item and ordered other children.</summary>
    public static CanonicalIdentityNode Slot(int orientation, CanonicalIdentityChild? item, IReadOnlyList<CanonicalIdentityNode> children) =>
        new(4, nodes: children ?? throw new ArgumentNullException(nameof(children)), orientation: orientation, slotItem: item);
    internal byte Kind { get; }
    internal IReadOnlyList<CanonicalIdentityNode> Nodes { get; }
    internal IReadOnlyList<CanonicalIdentityChild> Items { get; }
    internal CanonicalIdentityShape? Shape { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal bool AlwaysStack { get; }
    internal int Orientation { get; }
    internal CanonicalIdentityChild? SlotItem { get; }
}
