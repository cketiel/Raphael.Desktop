using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Web.WebView2.Core;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Help;

namespace Raphael.Desktop.Views.Help;

/// <summary>
/// The help panel: the shipped bundle in a WebView, plus the control that moves it between a tab
/// and its own window.
/// </summary>
public partial class HelpView : UserControl, INotifyPropertyChanged
{
    private string _topic;
    private string _language;
    private bool _ready;
    private bool _isInOwnWindow;

    public HelpView()
    {
        InitializeComponent();

        StatusText.Text = LocalizationService.Instance["HelpLoading"];

        Loaded += OnLoaded;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Raised when a page asks the application to do something ("open it for me").</summary>
    public event EventHandler<string> ActionRequested;

    /// <summary>Set by whoever owns the panel; moves it between a tab and its own window.</summary>
    public Action ToggleWindowRequested { get; set; }

    /// <summary>The topic currently on screen, so a language switch can reload the same page.</summary>
    public string CurrentTopic => _topic;

    /// <summary>
    /// Where the panel is living right now.
    /// </summary>
    /// <remarks>
    /// The control has to say what it is going to do, and that is not the same in both places:
    /// from a tab it opens a window, from a window it puts the panel back.
    /// </remarks>
    public bool IsInOwnWindow
    {
        get => _isInOwnWindow;
        set
        {
            if (_isInOwnWindow == value)
                return;

            _isInOwnWindow = value;

            ToggleWindowIcon.Kind = value ? PackIconKind.DockWindow : PackIconKind.OpenInNew;
            OnPropertyChanged(nameof(ToggleWindowText));
        }
    }

    public string ToggleWindowText =>
        LocalizationService.Instance[_isInOwnWindow ? "HelpDockBack" : "HelpToggleWindow"];

    /// <summary>Shows a topic, navigating only when something actually changed.</summary>
    public void Navigate(string topicId, string language)
    {
        var sameAsBefore = topicId == _topic && language == _language;

        _topic = topicId;
        _language = language;

        if (_ready && !sameAsBefore)
            NavigateNow();
    }

    /// <summary>
    /// Reloads the page in another language.
    /// </summary>
    /// <remarks>
    /// The application switches language without restarting, and the help has to follow it in the
    /// same breath. A help window still in English behind an interface that just turned Spanish is
    /// the moment a reader stops believing the two are the same product.
    /// </remarks>
    public void SwitchLanguage(string language)
    {
        if (language == _language)
            return;

        _language = language;

        StatusText.Text = LocalizationService.Instance["HelpLoading"];
        OnPropertyChanged(nameof(ToggleWindowText));

        if (_ready)
            NavigateNow();
    }

    private void NavigateNow()
    {
        if (string.IsNullOrWhiteSpace(_topic))
            return;

        Browser.CoreWebView2.Navigate(HelpService.BuildUrl(_topic, _language));
    }

    private void ToggleWindowButton_Click(object sender, RoutedEventArgs e) =>
        ToggleWindowRequested?.Invoke();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ready)
            return;

        try
        {
            // The user profile lives beside the rest of the application's data rather than next to
            // the executable: Program Files is not writable for the account a dispatcher runs as,
            // and WebView2 refuses to start when it cannot create it.
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RapphaelApp",
                "HelpWebView");

            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await Browser.EnsureCoreWebView2Async(environment);

            var core = Browser.CoreWebView2;

            core.SetVirtualHostNameToFolderMapping(
                HelpService.VirtualHostName,
                HelpService.BundleRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            // A help panel is for reading the help. Anything a page can do beyond that is surface
            // nobody asked for, on a machine with a dispatch session open.
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;

            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += OnNavigationCompleted;
            core.WebMessageReceived += OnWebMessageReceived;

            _ready = true;
            StatusText.Visibility = Visibility.Collapsed;

            NavigateNow();
        }
        catch (Exception exception)
        {
            // WebView2 missing from the machine is the realistic case, and it is not worth a crash
            // dialog: the application works without help, it just cannot show it.
            FileLogger.Log($"The help panel could not start WebView2: {exception}");
            StatusText.Text = LocalizationService.Instance["HelpUnavailable"];
        }
    }

    /// <summary>
    /// Keeps the panel inside the bundle.
    /// </summary>
    /// <remarks>
    /// The pages are ours and carry no outside links today, but this panel has no address bar and
    /// no way back: one page that navigated away would strand the reader with no route home.
    /// </remarks>
    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Host, HelpService.VirtualHostName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
    }

    /// <summary>An external link opens in the real browser, not in a second chromeless window.</summary>
    private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
            return;

        if (uri.Scheme is not ("http" or "https"))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            FileLogger.Log($"An external help link could not be opened: {exception.Message}");
        }
    }

    /// <summary>
    /// Tells the page which application it is running inside.
    /// </summary>
    /// <remarks>
    /// This is what turns a static page into help that knows its own limits: with the real version
    /// in hand, a bundle written for an older release says so in a band at the top instead of
    /// quietly describing a product that has moved on.
    /// </remarks>
    private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            FileLogger.Log($"The help page did not load: {e.WebErrorStatus}");
            return;
        }

        try
        {
            var state = HelpService.Instance.BuildHostState();
            await Browser.CoreWebView2.ExecuteScriptAsync($"window.__helpSetHost && window.__helpSetHost({state});");
        }
        catch (Exception exception)
        {
            FileLogger.Log($"The help page could not be told about the host: {exception.Message}");
        }
    }

    /// <summary>Handles the page's "open it for me" links.</summary>
    private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);

            if (!document.RootElement.TryGetProperty("type", out var type))
                return;

            if (type.GetString() != "help.action")
                return;

            if (!document.RootElement.TryGetProperty("action", out var action))
                return;

            var value = action.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                ActionRequested?.Invoke(this, value);
        }
        catch (Exception exception)
        {
            FileLogger.Log($"A message from the help page could not be read: {exception.Message}");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
