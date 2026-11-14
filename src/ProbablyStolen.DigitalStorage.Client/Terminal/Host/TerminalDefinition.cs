using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>
/// A terminal view one terminal item can switch to.
/// </summary>
public sealed class TerminalDefinition
{
    /// <summary>Creates a definition.</summary>
    /// <param name="terminalName">The unique name, stored as the current terminal.</param>
    /// <param name="order">The sort order; ties sort by name.</param>
    /// <param name="translationKey">The key of the display name.</param>
    /// <param name="isAvailable">Whether the view can be used in a context, for example with its module installed.</param>
    /// <param name="createView">Creates the platform view for a platform window. Client code does not know either type.</param>
    public TerminalDefinition(string terminalName, int order, string translationKey,
        Func<TerminalContext, bool> isAvailable, Func<object, object> createView)
    {
        TerminalName = string.IsNullOrEmpty(terminalName)
            ? throw new ArgumentException("A terminal name is required.", nameof(terminalName))
            : terminalName;
        Order = order;
        TranslationKey = string.IsNullOrEmpty(translationKey)
            ? throw new ArgumentException("A translation key is required.", nameof(translationKey))
            : translationKey;
        IsAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
        CreateView = createView ?? throw new ArgumentNullException(nameof(createView));
    }

    /// <summary>Gets the unique name.</summary>
    public string TerminalName { get; }

    /// <summary>Gets the sort order.</summary>
    public int Order { get; }

    /// <summary>Gets the key of the display name.</summary>
    public string TranslationKey { get; }

    /// <summary>Gets whether the view can be used in a context.</summary>
    public Func<TerminalContext, bool> IsAvailable { get; }

    /// <summary>Gets the platform view factory; its argument is <see cref="TerminalContext.Window"/>.</summary>
    public Func<object, object> CreateView { get; }
}
