using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Views.Help;

namespace Raphael.Desktop.Services.Help;

/// <summary>
/// Resolves help topics to pages in the shipped bundle and keeps the single help panel.
/// </summary>
/// <remarks>
/// The bundle lives beside the executable, in <c>Assets\Help</c>, and is compiled from the
/// ecosystem repository at release time (<c>/ship</c> step 4-bis). Nothing here reaches the
/// network: a dispatch office with no internet still has its help.
/// </remarks>
public sealed class HelpService : IHelpService
{
    /// <summary>
    /// Virtual host the bundle is served from inside WebView2.
    /// </summary>
    /// <remarks>
    /// Not a <c>file://</c> path, deliberately. Pages loaded from the file system get an opaque
    /// origin, which takes <c>localStorage</c> away from them and makes the theme preference
    /// impossible to remember. Mapping a host name gives the bundle one stable origin, and it is
    /// the same shape of URL the Portal will serve later, so the pages do not have to know where
    /// they are running.
    /// </remarks>
    private const string VirtualHost = "raphael.help";

    private const string AppName = "Raphael.Desktop";

    private static readonly Lazy<HelpService> Lazy = new(() => new HelpService());

    public static HelpService Instance => Lazy.Value;

    private HelpView _view;
    private HelpManifest _manifest;
    private bool _manifestRead;

    private HelpService()
    {
        // The interface switches language without restarting, and the help has to follow in the
        // same breath. A panel still in English behind a Spanish interface is the moment a reader
        // stops believing the two are the same product.
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Absolute path of the shipped bundle.</summary>
    public static string BundleRoot =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Help");

    public static string VirtualHostName => VirtualHost;

    public event EventHandler<string> ActionRequested;

    /// <summary>Set by the main window: makes the panel visible, in a tab or in its own window.</summary>
    public Action ShowRequested { get; set; }

    /// <summary>Set by the main window: moves the panel between a tab and its own window.</summary>
    public Action ToggleWindowRequested { get; set; }

    /// <summary>
    /// Set by the main window: hands back the content of the tab in front.
    /// </summary>
    /// <remarks>
    /// This is what F1 reads first, and it exists because the keyboard focus is a bad witness.
    /// Clicking the bell opens the inbox but leaves focus on a button in the title bar, outside
    /// every view — so an answer based on focus alone would describe whatever tab was opened
    /// before, which is exactly the wrong page at exactly the wrong moment.
    /// </remarks>
    public Func<DependencyObject> ActiveContentProvider { get; set; }

    public bool IsAvailable => Manifest is not null;

    /// <summary>The application version the shipped help was written against, or null.</summary>
    public string CoveredVersion =>
        Manifest is not null && Manifest.CoversApp.TryGetValue(AppName, out var version)
            ? version
            : null;

    /// <summary>The day the bundle was compiled, as yyyy-MM-dd, or null.</summary>
    public string BuiltOn => Manifest?.BuiltUtc is { Length: >= 10 } built ? built[..10] : null;

    /// <summary>Short commit of the ecosystem repository the bundle came from, or null.</summary>
    public string SourceCommit => Manifest?.SourceCommit;

    /// <summary>The single help panel. Created the first time somebody asks for help.</summary>
    public HelpView View
    {
        get
        {
            if (_view is not null)
                return _view;

            _view = new HelpView();
            _view.ToggleWindowRequested = () => ToggleWindowRequested?.Invoke();
            _view.ActionRequested += (_, action) => ActionRequested?.Invoke(this, action);

            return _view;
        }
    }

    /// <summary>
    /// The manifest, read once.
    /// </summary>
    /// <remarks>
    /// A missing or unreadable bundle is not an error worth stopping anybody for: it means help is
    /// not available in this build, and the entry points say so instead of throwing at a dispatcher
    /// who pressed F1.
    /// </remarks>
    private HelpManifest Manifest
    {
        get
        {
            if (_manifestRead)
                return _manifest;

            _manifestRead = true;

            try
            {
                var path = Path.Combine(BundleRoot, "help.manifest.json");
                if (!File.Exists(path))
                    return null;

                _manifest = JsonSerializer.Deserialize<HelpManifest>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                FileLogger.Log($"Help manifest could not be read: {exception.Message}");
                _manifest = null;
            }

            return _manifest;
        }
    }

    public void OpenForMenu(MENU menu) => Open(HelpTopics.ForMenu(menu));

    public void OpenContextual(MENU? fallbackMenu) => Open(ResolveContextualTopic(fallbackMenu));

    public void Open(string topicId)
    {
        if (Manifest is null)
        {
            MessageBox.Show(
                LocalizationService.Instance["HelpUnavailable"],
                LocalizationService.Instance["Help"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var language = ResolveLanguage();
        var resolved = ResolveTopic(topicId, language);

        // Visible first, navigated second: the panel has to have a parent before WebView2 will
        // initialise, and until it does there is nothing to navigate.
        ShowRequested?.Invoke();

        View.Navigate(resolved, language);
    }

    /// <summary>
    /// Works out which topic answers for what the dispatcher is looking at.
    /// </summary>
    /// <remarks>
    /// Order matters, and each step is here because of a way the previous one gets it wrong:
    ///
    /// <list type="number">
    /// <item>F1 pressed inside the help itself is a question about the help, not a request to
    /// reopen the page already on screen.</item>
    /// <item>The focused element, but only when it really belongs to the active window. It gives
    /// the deepest declaration, which is what makes a dialog win over the tab behind it.</item>
    /// <item>The content of the tab in front. A tab's content is a plain Grid holding the view, and
    /// the view is a child, so this has to look down rather than up.</item>
    /// <item>The window itself, for a dialog that declares its topic on the root.</item>
    /// <item>The last main-menu tab that was opened, and finally the home topic.</item>
    /// </list>
    /// </remarks>
    private string ResolveContextualTopic(MENU? fallbackMenu)
    {
        var active = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive);

        if (active is HelpWindow)
            return HelpTopics.UsingHelp;

        var topic = ResolveFocusWithin(active);

        if (topic is null && active is MainWindow)
            topic = HelpAssist.ResolveInSubtree(ActiveContentProvider?.Invoke());

        if (topic is null && active is not null && active is not MainWindow)
            topic = HelpAssist.ResolveUpwards(active) ?? HelpAssist.ResolveInSubtree(active);

        return topic
               ?? (fallbackMenu.HasValue ? HelpTopics.ForMenu(fallbackMenu.Value) : HelpTopics.Home);
    }

    /// <summary>
    /// The topic declared above the focused element, ignoring focus that lives in another window.
    /// </summary>
    private static string ResolveFocusWithin(Window window)
    {
        if (window is null || Keyboard.FocusedElement is not DependencyObject focused)
            return null;

        return ReferenceEquals(Window.GetWindow(focused), window)
            ? HelpAssist.ResolveUpwards(focused)
            : null;
    }

    /// <summary>
    /// Turns a requested id into one that actually exists in this bundle.
    /// </summary>
    /// <remarks>
    /// Follows a redirect first, because an id that shipped once is a contract and the corpus
    /// promises to keep answering for it. If even that misses — an older executable asking for a
    /// topic this bundle does not carry — it lands on the home topic rather than a blank page.
    /// </remarks>
    private string ResolveTopic(string topicId, string language)
    {
        if (string.IsNullOrWhiteSpace(topicId))
            return Manifest.Home ?? HelpTopics.Home;

        if (Manifest.Redirects.TryGetValue(topicId, out var target))
            topicId = target;

        if (PageExists(topicId, language))
            return topicId;

        FileLogger.Log($"Help topic '{topicId}' is not in this bundle; opening the home topic.");
        return Manifest.Home ?? HelpTopics.Home;
    }

    private static bool PageExists(string topicId, string language) =>
        File.Exists(Path.Combine(BundleRoot, language, $"{topicId.Replace('/', Path.DirectorySeparatorChar)}.html"));

    /// <summary>The help follows the language the application is showing, not the machine's.</summary>
    private string ResolveLanguage()
    {
        var language = LocalizationService.Instance.CurrentLanguage;

        if (Manifest is not null && Manifest.Languages.Count > 0 && !Manifest.Languages.Contains(language))
            language = Manifest.Languages[0];

        return language;
    }

    /// <summary>Reloads the panel when the application changes language, if it is on screen.</summary>
    private void OnLanguageChanged()
    {
        if (_view is null)
            return;

        _view.Dispatcher.Invoke(() => _view.SwitchLanguage(ResolveLanguage()));
    }

    /// <summary>Builds the URL a topic is served at inside the WebView.</summary>
    public static string BuildUrl(string topicId, string language) =>
        $"https://{VirtualHost}/{language}/{topicId}.html";

    /// <summary>
    /// What the page is told about the application it is running inside.
    /// </summary>
    /// <remarks>
    /// This is what lets a page know it is behind the application and say so. Everything here is
    /// operational, never clinical: no patient, no trip, nothing that could end up pasted into a
    /// support email by somebody following the page's own advice.
    /// </remarks>
    public string BuildHostState(string channel = null)
    {
        var state = new
        {
            appVersion = VersionHelper.Version,
            build = VersionHelper.Build,
            role = MapRole(SessionManager.Role),
            language = ResolveLanguage(),
            channel = channel ?? "unknown",
            theme = "light"
        };

        return JsonSerializer.Serialize(state);
    }

    /// <summary>Maps the numeric role of the session onto the names the help corpus uses.</summary>
    private static string MapRole(string role) => role switch
    {
        "1" => "admin",
        "2" => "driver",
        _ => "dispatcher"
    };
}
