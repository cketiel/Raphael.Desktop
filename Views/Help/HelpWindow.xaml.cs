using System.Globalization;
using System.Windows;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Views.Help;

/// <summary>
/// Hosts the help panel when it is pulled out of the tab strip.
/// </summary>
/// <remarks>
/// Holds no help logic of its own: it is a frame around <see cref="HelpView"/>, which is the same
/// instance the tab was holding. All this window adds is remembering where the reader put it —
/// which matters more than it sounds, because the usual place is the second monitor, and a window
/// that reopens centred on the first one has to be dragged back every single time.
/// </remarks>
public partial class HelpWindow : Window
{
    private readonly HelpView _panel;

    public HelpWindow(HelpView panel, Window owner)
    {
        InitializeComponent();

        _panel = panel;

        Title = LocalizationService.Instance["Help"];

        if (owner is not null && !ReferenceEquals(owner, this))
            Owner = owner;

        Host.Children.Add(panel);

        RestoreGeometry();

        // The title bar has to follow the language too. The panel reloads its page on its own;
        // a window still captioned "Help" over a Spanish page is the same broken seam, smaller.
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        Closing += OnClosing;
    }

    private void OnLanguageChanged() =>
        Dispatcher.Invoke(() => Title = LocalizationService.Instance["Help"]);

    private bool _movingToTab;

    /// <summary>Raised when the window closes because the panel is on its way into a tab.</summary>
    public event EventHandler DockRequested;

    /// <summary>Raised when the reader simply closed the window.</summary>
    public event EventHandler Dismissed;

    /// <summary>
    /// Closes the window because the panel is being docked, not because the reader is done.
    /// </summary>
    /// <remarks>
    /// The two gestures look identical from here — the window goes away either way — and telling
    /// them apart is the whole point. Closing the help means "I have finished reading"; pressing
    /// the dock control means "keep it, over there". Treating the first as the second is what made
    /// the close button reopen the help in a tab, and quietly rewrote which of the two the reader
    /// had actually chosen.
    /// </remarks>
    public void CloseForDocking()
    {
        _movingToTab = true;
        Close();
    }

    /// <summary>
    /// Puts the window back where it was last left.
    /// </summary>
    /// <remarks>
    /// The saved rectangle is checked against the screens that exist *now*. A dispatcher who
    /// worked on a docked laptop and then took it home would otherwise get a help window
    /// positioned on a monitor that is no longer plugged in — visible to Windows, invisible to
    /// them, and impossible to drag back.
    /// </remarks>
    private void RestoreGeometry()
    {
        var saved = Properties.Settings.Default.HelpWindowBounds;

        if (!string.IsNullOrWhiteSpace(saved))
        {
            try
            {
                var bounds = Rect.Parse(saved);

                if (bounds.Width >= MinWidth && bounds.Height >= MinHeight && IsOnAScreen(bounds))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = bounds.X;
                    Top = bounds.Y;
                    Width = bounds.Width;
                    Height = bounds.Height;
                }
            }
            catch (Exception exception)
            {
                FileLogger.Log($"The saved help window position could not be read: {exception.Message}");
            }
        }

        if (Properties.Settings.Default.HelpWindowMaximized)
            WindowState = WindowState.Maximized;
    }

    /// <summary>True when a good part of the rectangle falls inside the virtual desktop.</summary>
    private static bool IsOnAScreen(Rect bounds)
    {
        var desktop = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var visible = Rect.Intersect(desktop, bounds);

        // A sliver hanging off the edge is not "on a screen": the title bar has to be reachable.
        return visible != Rect.Empty && visible.Width >= 200 && visible.Height >= 120;
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // The service outlives this window, so a window that stayed subscribed would be kept alive
        // by the event and would go on renaming a title bar nobody can see.
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

        SaveGeometry();

        // Hand the panel back before the window goes: a UserControl can only have one parent, and
        // whoever takes it next has to find it unattached.
        Host.Children.Remove(_panel);

        if (_movingToTab)
            DockRequested?.Invoke(this, EventArgs.Empty);
        else
            Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveGeometry()
    {
        try
        {
            // RestoreBounds, not Left/Top/Width/Height: while maximized those report the maximized
            // rectangle, and reopening would restore a window welded to the full screen.
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            if (bounds.Width > 0 && bounds.Height > 0)
            {
                Properties.Settings.Default.HelpWindowBounds =
                    bounds.ToString(CultureInfo.InvariantCulture);
            }

            Properties.Settings.Default.HelpWindowMaximized = WindowState == WindowState.Maximized;
            Properties.Settings.Default.Save();
        }
        catch (Exception exception)
        {
            FileLogger.Log($"The help window position could not be saved: {exception.Message}");
        }
    }
}
