namespace Raphael.Desktop.Services.Help;

/// <summary>
/// Opens the product help at a given topic.
/// </summary>
public interface IHelpService
{
    /// <summary>True when a help bundle was shipped with this build and could be read.</summary>
    bool IsAvailable { get; }

    /// <summary>The application version the shipped help was written against, or null.</summary>
    string CoveredVersion { get; }

    /// <summary>Raised when a page asks the application to do something ("open it for me").</summary>
    event EventHandler<string> ActionRequested;

    /// <summary>Opens the help at <paramref name="topicId"/>, or at the closest thing to it.</summary>
    void Open(string topicId);

    /// <summary>Opens the help at the topic that answers for a main-menu tab.</summary>
    void OpenForMenu(MENU menu);

    /// <summary>
    /// Opens the topic declared closest to whatever currently has focus, falling back to
    /// <paramref name="fallbackMenu"/>. This is what F1 calls.
    /// </summary>
    void OpenContextual(MENU? fallbackMenu);
}
