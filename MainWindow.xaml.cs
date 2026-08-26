using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;
using Raphael.Desktop.ViewModels;
using Raphael.Desktop.Views;
using Raphael.Desktop.Views.Notifications;

namespace Raphael.Desktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer themeTimer;
        private int tabCounter = 1;

        private readonly INotificationService _notificationService;
        private readonly NotificationTextService _notificationTextService;

        private readonly MainWindowViewModel _viewModel;


        private NotificationCenterView? _notificationCenter;
        private NotificationCenterWindow? _notificationCenterWindow;
        private NotificationToastViewModel? _toasts;
        private NotificationAlertWindow? _notificationAlertWindow;

        /// <summary>
        /// Shutting down closes the owned panel window, which asks for the panel back.
        /// Without this flag the application would try to open a tab on its way out.
        /// </summary>
        private bool _isClosing;

        public MainWindow()
        {
            InitializeComponent();

            Title = VersionHelper.WindowTitle;

            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

            // One inbox for the whole application: the bell badge and the Notification
            // Center read the same list, so they cannot disagree.
            _notificationService = new NotificationService(
                new NotificationApiClient(),
                new NotificationSignalRService(),
                new LocalNotificationReadStateStore());

            _notificationTextService = new NotificationTextService();

            _toasts = new NotificationToastViewModel(
                _notificationTextService,
                ShowNotificationInCenter,
                OpenNotificationCenter,
                IsNotificationCenterInFront,
                () => NativeWindowInterop.FlashTaskbar(this));

            NotificationToastHostControl.DataContext = _toasts.Inline;

            // The floating window is built once and hides itself while there is nothing to
            // show, instead of being created and destroyed per alert.
            _notificationAlertWindow = new NotificationAlertWindow(this, _toasts.Floating);

            _notificationAlertWindow.FootprintChanged +=
                (_, height) => LiftInlineAlerts(height);

            // Whatever the taskbar was blinking about, the dispatcher is here now.
            Activated += (_, _) => NativeWindowInterop.StopFlashing(this);

            _viewModel.InitializeNotifications(
                _notificationService,
                OpenNotificationCenter,
                _toasts.Show);

            string currentRole = SessionManager.Role;
            bool IsDriver = currentRole == "2";
            if (IsDriver)
                OpenTab(LocalizationService.Instance["Reports"], new ReportsView(), PackIconKind.FileChart);
            else
                OpenHomeView(null, null); // Load HomeView by default
            this.Loaded += MainWindow_Loaded;
            this.Closing += (_, _) => _isClosing = true;
            this.Closed += MainWindow_Closed;

            /*UpdateTheme();

            themeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            themeTimer.Tick += (s, e) => UpdateTheme();
            themeTimer.Start();*/
        }

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            // Set to full screen
            this.WindowState = WindowState.Maximized;
            //this.WindowStyle = WindowStyle.None;
            //this.Topmost = true; // To keep the window always in front

            UserNameTextBlock.Text = SessionManager.Username;

            await _viewModel.StartNotificationsAsync();
        }

        private async void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            try
            {
                _notificationCenterWindow?.Close();

                // No owner, so nothing else would ever close it — and an open window with
                // no owner keeps the process alive after the main one is gone.
                _notificationAlertWindow?.Shutdown();

                _toasts?.Close();

                _notificationCenter?.ViewModel.Close();

                await _viewModel.StopNotificationsAsync();
            }
            catch
            {
                // Ignore errors while closing the application.
            }
        }

        #region Notification Center

        /// <summary>
        /// Opens the Notification Center, or brings it to the front if it is already open.
        /// </summary>
        /// <remarks>
        /// One panel, never two. Two would each keep their own selection over the same
        /// inbox, and a dispatcher would have no way to tell which one they had acted on.
        /// </remarks>
        private void OpenNotificationCenter()
        {
            // Already pulled out to its own window: bring that one forward.
            if (_notificationCenterWindow is not null)
            {
                _notificationCenterWindow.Activate();
                return;
            }

            var title = LocalizationService.Instance["NotificationCenter"];

            var existing = MainTabControl.Items
                .OfType<TabItem>()
                .FirstOrDefault(tab => tab.Tag?.ToString() == title);

            if (existing is not null)
            {
                MainTabControl.SelectedItem = existing;
                return;
            }

            _notificationCenter ??= CreateNotificationCenter();

            OpenTab(title, _notificationCenter, PackIconKind.BellOutline);
        }

        private NotificationCenterView CreateNotificationCenter()
        {
            var panel = new NotificationCenterView(
                _notificationService,
                _notificationTextService);

            panel.ViewModel.ToggleWindowRequested = ToggleNotificationCenterWindow;

            panel.ViewModel.OpenTripRequested = OpenTripInDispatch;

            panel.ViewModel.AlertSettingsRequested = OpenAlertSettings;

            return panel;
        }

        /// <summary>
        /// Moves the panel between a tab and its own window, and back.
        /// </summary>
        /// <remarks>
        /// The very same UserControl instance is handed over, so the selection, the scroll
        /// position and the trip already loaded survive the move. A UserControl can only
        /// have one parent, hence the detach before the re-attach.
        /// </remarks>
        private void ToggleNotificationCenterWindow()
        {
            if (_notificationCenter is null)
                return;

            if (_notificationCenterWindow is not null)
            {
                _notificationCenterWindow.Close();
                return;
            }

            var title = LocalizationService.Instance["NotificationCenter"];

            var tab = MainTabControl.Items
                .OfType<TabItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == title);

            if (tab is not null)
            {
                if (tab.Content is Grid host)
                    host.Children.Remove(_notificationCenter);

                MainTabControl.Items.Remove(tab);
            }

            _notificationCenterWindow =
                new NotificationCenterWindow(_notificationCenter, this);

            // The pop-out control has to say what it will do, and it is not the same thing
            // in both places: from a tab it opens a window, from the window it puts the
            // panel back. Same for its icon.
            _notificationCenter.ViewModel.IsInOwnWindow = true;

            _notificationCenterWindow.ReturnRequested += (_, _) =>
            {
                _notificationCenterWindow = null;

                if (_notificationCenter is not null)
                    _notificationCenter.ViewModel.IsInOwnWindow = false;

                // Closing the window returns the panel to the tab strip instead of losing
                // it: a dispatcher who closes it by reflex has not thrown their inbox away.
                if (IsLoaded && !_isClosing)
                    OpenNotificationCenter();
            };

            _notificationCenterWindow.Show();
        }

        /// <summary>
        /// Opens the Dispatch screen for the trip a notification is about.
        /// </summary>
        /// <remarks>
        /// ⚠️ It opens Dispatch, it does not yet select the trip: DispatchView has no way
        /// of being told which one to go to. The trip number is on screen in the detail
        /// pane, and Copy trip number puts it on the clipboard, so the dispatcher can
        /// search for it. Deep linking is registered in <c>_meta/BACKLOG.md</c>.
        /// </remarks>
        private void OpenTripInDispatch(int tripId)
        {
            OpenTab(
                LocalizationService.Instance["Dispatch"],
                new DispatchView(),
                PackIconKind.WrenchClock);
        }

        #endregion

        #region Live alerts

        /// <summary>
        /// Opens the Notification Center on the notice the dispatcher clicked.
        /// </summary>
        /// <remarks>
        /// The alert card is not a dead end: from it, one click lands on the notice itself
        /// instead of leaving the dispatcher to remember it, close it, open the bell and
        /// hunt for the row.
        /// </remarks>
        private void ShowNotificationInCenter(Models.NotificationDto notification)
        {
            if (notification is null)
                return;

            // The floating window never takes the focus, so coming forward has to be asked
            // for. Here it is what the dispatcher meant by clicking.
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();

            OpenNotificationCenter();

            _notificationCenter?.ViewModel.ShowNotification(notification.Id);
        }

        /// <summary>
        /// True when the dispatcher is already looking at the inbox.
        /// </summary>
        /// <remarks>
        /// Announcing what is on screen in front of them is noise. Only the ambient level
        /// is held back by this: a Will Call still shows, because the panel does not tell
        /// them the clock has started.
        /// </remarks>
        private bool IsNotificationCenterInFront()
        {
            if (_notificationCenterWindow is not null)
                return _notificationCenterWindow.IsVisible;

            if (!IsActive)
                return false;

            var title = LocalizationService.Instance["NotificationCenter"];

            return MainTabControl.SelectedItem is TabItem tab &&
                   tab.Tag?.ToString() == title;
        }

        /// <summary>
        /// Moves the in-window stack above the floating alert, so neither covers the other.
        /// </summary>
        private void LiftInlineAlerts(double floatingHeight)
        {
            var bottom = floatingHeight > 0
                ? 25 + floatingHeight + 12
                : 25;

            NotificationToastHostControl.Margin = new Thickness(0, 0, 25, bottom);
        }

        /// <summary>Personal alert preferences, from the Notification Center toolbar.</summary>
        private void OpenAlertSettings()
        {
            if (_toasts is null)
                return;

            var window = new NotificationAlertSettingsWindow(_toasts)
            {
                Owner = this
            };

            window.ShowDialog();
        }

        #endregion

        private void Window_KeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F11)  // Use F11 to toggle mode
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    this.WindowState = WindowState.Normal;
                    this.WindowStyle = WindowStyle.SingleBorderWindow;
                }
                else
                {
                    this.WindowState = WindowState.Maximized;
                    this.WindowStyle = WindowStyle.None;
                }
            }
        }

        private void OpenHomeView(object sender, RoutedEventArgs e)
        {
            //MainContent.Content = new HomeView();
            OpenTab(LocalizationService.Instance["Home"], new HomeView(), PackIconKind.HomeOutline);

            //SetActiveMenu(btnHome);
            CloseAllTabsOfType("Admin");
        }

        private void OpenAdminView(object sender, RoutedEventArgs e)
        {
            //MainContent.Content = new AdminView();
            OpenTab("Admin", new AdminView(), PackIconKind.AccountBoxOutline);

            //SetActiveMenu(btnAdmin);
        }

        private void OpenTab(
            string title,
            UserControl content,
            PackIconKind iconKind)
        {
            var tabItem = new TabItem
            {
                Tag = title // Guarda el tipo, como "Admin", "Home"
            };

            // --- ANIMACIÓN de aparición del contenido ---
            var fadeIn = new DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(300));

            var contentGrid = new Grid();

            // Most screens hand over a brand new control, but the Notification Center hands
            // back the same instance every time so the inbox keeps its state. Whatever was
            // holding it before has to let go first.
            DetachFromParent(content);

            contentGrid.Children.Add(content);
            contentGrid.BeginAnimation(
                OpacityProperty,
                fadeIn);

            // --- HEADER con icono y botón cerrar ---
            var dockPanel = new DockPanel
            {
                LastChildFill = false
            };

            var icon = new PackIcon
            {
                Kind = iconKind,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = $"{title}", // {tabCounter++}",
                VerticalAlignment = VerticalAlignment.Center
            };

            var contentStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            contentStack.Children.Add(icon);
            contentStack.Children.Add(text);

            DockPanel.SetDock(contentStack, Dock.Left);
            dockPanel.Children.Add(contentStack);

            var closeIcon = new PackIcon
            {
                Kind = PackIconKind.Close,
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };

            var closeBtn = new Button
            {
                Content = closeIcon,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(4),
                ToolTip = "Cerrar",
                Width = 22,
                Height = 22
            };

            closeBtn.MouseEnter +=
                (s, e) => closeIcon.Foreground = Brushes.Red;

            closeBtn.MouseLeave +=
                (s, e) => closeIcon.Foreground = Brushes.Gray;

            closeBtn.Click +=
                (s, e) => CloseTabWithAnimation(tabItem);

            DockPanel.SetDock(closeBtn, Dock.Right);
            dockPanel.Children.Add(closeBtn);

            tabItem.Header = dockPanel;
            tabItem.Content = contentGrid;

            // --- CONTEXT MENU ---
            var contextMenu = new ContextMenu();

            var closeThis = new MenuItem
            {
                Header = "Close this tab"
            };

            closeThis.Click +=
                (s, e) => CloseTabWithAnimation(tabItem);

            var closeOthers = new MenuItem
            {
                Header = "Close all except this one"
            };

            closeOthers.Click +=
                (s, e) =>
                {
                    var others =
                        MainTabControl.Items
                            .Cast<TabItem>()
                            .Where(t => t != tabItem)
                            .ToList();

                    foreach (var tab in others)
                        CloseTabWithAnimation(tab);
                };

            var closeAll = new MenuItem
            {
                Header = "Close all"
            };

            closeAll.Click +=
                (s, e) =>
                {
                    var allTabs =
                        MainTabControl.Items
                            .Cast<TabItem>()
                            .ToList();

                    foreach (var tab in allTabs)
                        CloseTabWithAnimation(tab);
                };

            var closeSameType = new MenuItem
            {
                Header = $"Close all '{title}'"
            };

            closeSameType.Click +=
                (s, e) =>
                {
                    var sameTabs =
                        MainTabControl.Items
                            .Cast<TabItem>()
                            .Where(t =>
                                t.Tag?.ToString() == title &&
                                t != tabItem)
                            .ToList();

                    foreach (var tab in sameTabs)
                        CloseTabWithAnimation(tab);
                };

            contextMenu.Items.Add(closeThis);
            contextMenu.Items.Add(closeOthers);
            contextMenu.Items.Add(closeAll);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(closeSameType);

            tabItem.ContextMenu = contextMenu;

            MainTabControl.Items.Add(tabItem);
            MainTabControl.SelectedItem = tabItem;
            MainTabControl.HorizontalContentAlignment =
                HorizontalAlignment.Left;

            /*MainTabControl.TabStripPlacement = Dock.Bottom;
            var style = this.FindResource("MaterialDesignFilledTabControl") as Style;
            if (style != null)
            {
                MainTabControl.Style = style;
            }*/
        }

        /// <summary>
        /// Fades a tab out and takes it off the strip.
        /// </summary>
        /// <remarks>
        /// The tab stops answering to its name straight away, even though it stays on
        /// screen for another fifth of a second. Otherwise clicking the bell during the
        /// fade finds the dying tab, selects it, and watches it disappear — the panel never
        /// opens and nothing explains why.
        /// </remarks>
        private void CloseTabWithAnimation(TabItem tabItem)
        {
            tabItem.Tag = null;

            if (tabItem.Content is Grid grid)
            {
                var fadeOut = new DoubleAnimation(
                    1,
                    0,
                    TimeSpan.FromMilliseconds(200));

                fadeOut.Completed +=
                    (s, e) =>
                    {
                        MainTabControl.Items.Remove(tabItem);

                        // The tab is gone, but the grid inside it still owns whatever it was
                        // showing. For a screen that is rebuilt every time this only frees
                        // memory; for the Notification Center it is the difference between
                        // reopening and an error dialog.
                        foreach (var child in grid.Children.OfType<UIElement>().ToList())
                        {
                            // A screen that listens to the inbox has to be told the tab is
                            // really closing. Unloaded cannot say it: in a TabControl that
                            // fires on every tab switch too, and a schedule panel that went
                            // deaf on the first switch would go back to offering cancelled
                            // trips as routable.
                            if (child is SchedulesView schedules)
                                schedules.ReleaseNotifications();

                            grid.Children.Remove(child);
                        }
                    };

                grid.BeginAnimation(
                    OpacityProperty,
                    fadeOut);
            }
            else
            {
                MainTabControl.Items.Remove(tabItem);
            }
        }

        /// <summary>
        /// Takes a control off whatever was holding it.
        /// </summary>
        /// <remarks>
        /// ⚠️ A WPF element has exactly one parent. The Notification Center is one instance
        /// that outlives its tab — the bell hands the very same control back so the
        /// selection, the scroll position and the loaded trip survive — so opening it a
        /// second time means adding an element that something else still claims. That is
        /// the "Specified element is already the logical child of another element.
        /// Disconnect it first." that closed the panel after a tab had been closed.
        /// </remarks>
        private static void DetachFromParent(UIElement element)
        {
            if (element is null)
                return;

            switch (LogicalTreeHelper.GetParent(element))
            {
                case Panel panel:
                    panel.Children.Remove(element);
                    break;

                case ContentControl host when ReferenceEquals(host.Content, element):
                    host.Content = null;
                    break;

                case Decorator decorator when ReferenceEquals(decorator.Child, element):
                    decorator.Child = null;
                    break;
            }
        }


        private void OpenTab2(
            string title,
            UserControl content,
            PackIconKind iconKind)
        {
            var tabItem = new TabItem();

            var headerPanel =
                new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

            var icon = new PackIcon
            {
                Kind = iconKind,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 5, 0)
            };

            var text = new TextBlock
            {
                Text = $"{title} {tabCounter++}",
                VerticalAlignment = VerticalAlignment.Center
            };

            var closeIcon = new PackIcon
            {
                Kind = PackIconKind.Close,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray,
                Margin = new Thickness(5, 0, 0, 0)
            };

            var closeBtn = new Button
            {
                Content = closeIcon,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(2),
                ToolTip = "Cerrar",
                Width = 24,
                Height = 24
            };

            closeBtn.Click +=
                (s, e) => MainTabControl.Items.Remove(tabItem);

            var headerStack =
                new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

            headerStack.Children.Add(icon);
            headerStack.Children.Add(text);
            headerStack.Children.Add(closeBtn);

            tabItem.Header = headerStack;
            tabItem.Content = content;

            MainTabControl.Items.Add(tabItem);
            MainTabControl.SelectedItem = tabItem;
        }

        /*private void SetActiveMenu(Button activeButton)
        {
            if (activeButton == null) return;

            foreach (var child in MenuPanel.Children)
            {
                if (child is Button btn)
                    btn.Tag = "Inactive";
            }
            activeButton.Tag = "Active";
        }*/

        private void CloseAllTabsOfType(string headerText)
        {
            var tabsToRemove = MainTabControl.Items
                .OfType<TabItem>()
                .Where(tab =>
                    tab.Header is StackPanel stack &&
                    stack.Children
                        .OfType<TextBlock>()
                        .Any(t => t.Text == headerText))
                .ToList();

            foreach (var tab in tabsToRemove)
                MainTabControl.Items.Remove(tab);
        }

        private void UpdateTheme()
        {
            ResourceDictionary themeResourceDictionary =
                GetThemeResourceDictionary();

            Theme theme =
                themeResourceDictionary.GetTheme();

            BaseTheme currentTheme =
                theme.GetBaseTheme();

            /*BaseTheme newTheme = currentTheme switch
            {
                BaseTheme.Light => BaseTheme.Dark,
                _ => BaseTheme.Light
            };
            theme.SetBaseTheme(newTheme);*/

            theme.SetBaseTheme(BaseTheme.Light);

            themeResourceDictionary.SetTheme(theme);


            /*var paletteHelper = new PaletteHelper();
            Theme theme = paletteHelper.GetTheme();

            var now = DateTime.Now.TimeOfDay;
            bool isNight = now >= new TimeSpan(18, 0, 0) || now < new TimeSpan(6, 0, 0);

            theme.SetBaseTheme(isNight ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);*/
        }

        private ResourceDictionary GetThemeResourceDictionary()
        {
            //We can't use PaletteHelper here because it will try to use Application.Current.Resource
            //Instead we need to give it the resource dictionary that contains the material design theme dictionaries.
            //In this case that is the theme dictionary inside of this Window's Resources.
            return Resources.MergedDictionaries
                .Single(x => x is IMaterialDesignThemeDictionary);
        }

        private void Logout()
        {
            SessionManager.Clear();

            var loginWindow = new LoginWindow();
            loginWindow.Show();

            this.Close();
        }

        // If you click twice on the same tab, this event is not fired.
        // This peculiarity does not allow you to open 2 tabs of the same type if you click twice in a row on the same tab.
        // The solution is to use the event instead: PreviewMouseLeftButtonUp
        private void MenuTabControl_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (MenuTabControl.SelectedItem is TabItem selectedTab &&
                selectedTab.Tag is MENU menu)
            {
                switch (menu)
                {
                    case MENU.Home:
                        OpenTab(
                            "Home",
                            new HomeView(),
                            PackIconKind.HomeOutline);
                        break;

                    case MENU.Admin:
                        OpenTab(
                            "Admin",
                            new AdminView(),
                            PackIconKind.AccountBoxOutline);
                        break;
                }
            }
        }

        private void MenuTabControl_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            var clickedElement =
                e.OriginalSource as DependencyObject;

            var tabItem =
                FindParent<TabItem>(clickedElement);

            if (tabItem != null &&
                tabItem.Tag is MENU selectedMenu)
            {
                // Validate if the user has permission before opening the tab
                string role = SessionManager.Role;

                if (selectedMenu == MENU.Admin &&
                    role != "1")
                    return;

                if ((selectedMenu == MENU.Data ||
                     selectedMenu == MENU.Schedules ||
                     selectedMenu == MENU.Dispatch ||
                     selectedMenu == MENU.Notification)
                     &&
                     (role != "1" && role != "3"))
                    return;

                switch (selectedMenu)
                {
                    case MENU.Home:
                        OpenTab(
                            LocalizationService.Instance["Home"],
                            new HomeView(),
                            PackIconKind.HomeOutline);
                        break;

                    case MENU.Data:
                        OpenTab(
                            LocalizationService.Instance["Data"],
                            new DataView(),
                            PackIconKind.Database);
                        break;

                    case MENU.Schedules:
                        OpenTab(
                            LocalizationService.Instance["Schedules"],
                            new SchedulesView(_notificationService),
                            PackIconKind.TableClock);
                        break;

                    case MENU.Dispatch:
                        OpenTab(
                            LocalizationService.Instance["Dispatch"],
                            new DispatchView(),
                            PackIconKind.WrenchClock);
                        break;

                    case MENU.Reports:
                        OpenTab(
                            LocalizationService.Instance["Reports"],
                            new ReportsView(),
                            PackIconKind.FileChart);
                        break;

                    case MENU.Admin:
                        OpenTab(
                            LocalizationService.Instance["Admin"],
                            new AdminView(),
                            PackIconKind.Security);
                        break;
                }
            }
        }

        // Utilidad para encontrar el padre del tipo especificado
        private T FindParent<T>(
            DependencyObject child)
            where T : DependencyObject
        {
            if (child == null)
                return null;

            DependencyObject parentObject =
                VisualTreeHelper.GetParent(child);

            if (parentObject == null)
                return null;

            if (parentObject is T parent)
                return parent;

            return FindParent<T>(parentObject);
        }
    }

    public enum MENU
    {
        Home,
        Data,
        Schedules,
        Dispatch,
        Reports,
        Admin,
        Notification
    }
}