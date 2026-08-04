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
using Raphael.Desktop.ViewModels;
using Raphael.Desktop.Views;

namespace Raphael.Desktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer themeTimer;
        private int tabCounter = 1;
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainWindowViewModel();

            OpenHomeView(null, null); // Load HomeView by default
            this.Loaded += MainWindow_Loaded;

            /*UpdateTheme();

            themeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            themeTimer.Tick += (s, e) => UpdateTheme();
            themeTimer.Start();*/
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Set to full screen
            this.WindowState = WindowState.Maximized;
            //this.WindowStyle = WindowStyle.None;
            //this.Topmost = true; // To keep the window always in front

            UserNameTextBlock.Text = SessionManager.Username;
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

        private void OpenTab(string title, UserControl content, PackIconKind iconKind)
        {
            var tabItem = new TabItem
            {
                Tag = title // Guarda el tipo, como "Admin", "Home"
            };

            // --- ANIMACIÓN de aparición del contenido ---
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            var contentGrid = new Grid();
            contentGrid.Children.Add(content);
            contentGrid.BeginAnimation(OpacityProperty, fadeIn);

            // --- HEADER con icono y botón cerrar ---
            var dockPanel = new DockPanel { LastChildFill = false };

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

            closeBtn.MouseEnter += (s, e) => closeIcon.Foreground = Brushes.Red;
            closeBtn.MouseLeave += (s, e) => closeIcon.Foreground = Brushes.Gray;

            closeBtn.Click += (s, e) => CloseTabWithAnimation(tabItem);

            DockPanel.SetDock(closeBtn, Dock.Right);
            dockPanel.Children.Add(closeBtn);

            tabItem.Header = dockPanel;
            tabItem.Content = contentGrid;

            // --- CONTEXT MENU ---
            var contextMenu = new ContextMenu();

            var closeThis = new MenuItem { Header = "Close this tab" };
            closeThis.Click += (s, e) => CloseTabWithAnimation(tabItem);

            var closeOthers = new MenuItem { Header = "Close all except this one" };
            closeOthers.Click += (s, e) =>
            {
                var others = MainTabControl.Items.Cast<TabItem>().Where(t => t != tabItem).ToList();
                foreach (var tab in others)
                    CloseTabWithAnimation(tab);
            };

            var closeAll = new MenuItem { Header = "Close all" };
            closeAll.Click += (s, e) =>
            {
                var allTabs = MainTabControl.Items.Cast<TabItem>().ToList();
                foreach (var tab in allTabs)
                    CloseTabWithAnimation(tab);
            };

            var closeSameType = new MenuItem { Header = $"Close all '{title}'" };
            closeSameType.Click += (s, e) =>
            {
                var sameTabs = MainTabControl.Items.Cast<TabItem>()
                    .Where(t => t.Tag?.ToString() == title && t != tabItem).ToList();

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
            MainTabControl.HorizontalContentAlignment = HorizontalAlignment.Left;
            /*MainTabControl.TabStripPlacement = Dock.Bottom;
            var style = this.FindResource("MaterialDesignFilledTabControl") as Style;
            if (style != null)
            {
                MainTabControl.Style = style;
            }*/

        }
        private void CloseTabWithAnimation(TabItem tabItem)
        {
            if (tabItem.Content is Grid grid)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, e) => MainTabControl.Items.Remove(tabItem);
                grid.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                MainTabControl.Items.Remove(tabItem);
            }
        }



        private void OpenTab2(string title, UserControl content, PackIconKind iconKind)
        {
            var tabItem = new TabItem();

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

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

            closeBtn.Click += (s, e) => MainTabControl.Items.Remove(tabItem);

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
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
                .Where(tab => tab.Header is StackPanel stack &&
                              stack.Children.OfType<TextBlock>().Any(t => t.Text == headerText))
                .ToList();

            foreach (var tab in tabsToRemove)
                MainTabControl.Items.Remove(tab);
        }

        private void UpdateTheme()
        {
            ResourceDictionary themeResourceDictionary = GetThemeResourceDictionary();
            Theme theme = themeResourceDictionary.GetTheme();

            BaseTheme currentTheme = theme.GetBaseTheme();
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
            return Resources.MergedDictionaries.Single(x => x is IMaterialDesignThemeDictionary);
        }

        private void Logout() {
            SessionManager.Clear();
            var loginWindow = new LoginWindow();
            loginWindow.Show();
            this.Close();

        }

        // If you click twice on the same tab, this event is not fired.
        // This peculiarity does not allow you to open 2 tabs of the same type if you click twice in a row on the same tab.
        // The solution is to use the event instead: PreviewMouseLeftButtonUp
        private void MenuTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MenuTabControl.SelectedItem is TabItem selectedTab && selectedTab.Tag is MENU menu)
            {
                switch (menu)
                {
                    case MENU.Home:
                        OpenTab("Home", new HomeView(), PackIconKind.HomeOutline);
                        break;
                    case MENU.Admin:
                        OpenTab("Admin", new AdminView(), PackIconKind.AccountBoxOutline);
                        break;
                }

            }
        }

        private void MenuTabControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var clickedElement = e.OriginalSource as DependencyObject;
            var tabItem = FindParent<TabItem>(clickedElement);

            if (tabItem != null && tabItem.Tag is MENU selectedMenu)
            {
                // Validate if the user has permission before opening the tab
                string role = SessionManager.Role;

                if (selectedMenu == MENU.Admin && role != "1") return;
                if ((selectedMenu == MENU.Data || selectedMenu == MENU.Schedules ||
                     selectedMenu == MENU.Dispatch || selectedMenu == MENU.Reports)
                     && (role != "1" && role != "3")) return;

                switch (selectedMenu)
                {
                    case MENU.Home:
                        OpenTab(LocalizationService.Instance["Home"], new HomeView(), PackIconKind.HomeOutline);
                        break;
                    case MENU.Data:
                        OpenTab(LocalizationService.Instance["Data"], new DataView(), PackIconKind.Database);
                        break;
                    case MENU.Schedules:
                        OpenTab(LocalizationService.Instance["Schedules"], new SchedulesView(), PackIconKind.TableClock);
                        break;
                    case MENU.Dispatch:
                        OpenTab(LocalizationService.Instance["Dispatch"], new DispatchView(), PackIconKind.WrenchClock);
                        break;
                    case MENU.Reports:
                        OpenTab(LocalizationService.Instance["Reports"], new ReportsView(), PackIconKind.FileChart);
                        break;
                    case MENU.Admin:
                        OpenTab(LocalizationService.Instance["Admin"], new AdminView(), PackIconKind.Security);
                        break;
                }
            }
        }

        // Utilidad para encontrar el padre del tipo especificado
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
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
        Admin
    }

}