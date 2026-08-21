using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DocumentFormat.OpenXml.Spreadsheet;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views;

namespace Raphael.Desktop.ViewModels
{
    public class MainWindowViewModel : BaseViewModel
    {
        private bool _isAdmin;

        public bool IsAdmin
        {
            get => _isAdmin;
            set => SetProperty(ref _isAdmin, value);
        }

        private bool _isBookingVisible;

        public bool IsBookingVisible
        {
            get => _isBookingVisible;
            set => SetProperty(ref _isBookingVisible, value);
        }

        private bool _isGeneralVisible;

        public bool IsGeneralVisible
        {
            get => _isGeneralVisible;
            set => SetProperty(ref _isGeneralVisible, value);
        }

        private bool _isDriverVisible;

        public bool IsDriverVisible
        {
            get => _isDriverVisible;
            set => SetProperty(ref _isDriverVisible, value);
        }

        public string LoggedUserName => "Hello, User"; // Example

        public ICommand ChangeLanguageCommand { get; }

        public ICommand LogoutCommand { get; }

        public ICommand SelectionChangedCommand { get; }

        public ICommand OpenNotificationCenterCommand { get; }

        #region Translation

        // Main menu
        public string MainMenuHome =>
            LocalizationService.Instance["Home"];

        public string MainMenuData =>
            LocalizationService.Instance["Data"];

        public string MainMenuSchedules =>
            LocalizationService.Instance["Schedules"];

        public string MainMenuDispatch =>
            LocalizationService.Instance["Dispatch"];

        public string MainMenuReports =>
            LocalizationService.Instance["Reports"];

        public string MainMenuAdmin =>
            LocalizationService.Instance["Admin"];


        public string Settings =>
            LocalizationService.Instance["Settings"];

        public string MenuChangeLanguage =>
            LocalizationService.Instance["MenuChangeLanguage"];

        public string MenuEnglishLanguage =>
            LocalizationService.Instance["MenuEnglishLanguage"];

        public string MenuSpanishLanguage =>
            LocalizationService.Instance["MenuSpanishLanguage"];

        public string MenuLogout =>
            LocalizationService.Instance["MenuLogout"];


        private ObservableCollection<LanguageOption> _languages;

        public ObservableCollection<LanguageOption> Languages
        {
            get => _languages;

            set
            {
                _languages = value;
                OnPropertyChanged();
            }
        }

        private LanguageOption _selectedLanguage;

        public LanguageOption SelectedLanguage
        {
            get => _selectedLanguage;

            set
            {
                _selectedLanguage = value;
                OnPropertyChanged();

                if (value != null)
                    ChangeLanguage(value.LanguageCode);
            }
        }

        private Visibility _englishCheckVisibility =
            Visibility.Visible;

        public Visibility EnglishCheckVisibility
        {
            get => _englishCheckVisibility;

            set
            {
                _englishCheckVisibility = value;
                OnPropertyChanged();
            }
        }

        private Visibility _spanishCheckVisibility =
            Visibility.Collapsed;

        public Visibility SpanishCheckVisibility
        {
            get => _spanishCheckVisibility;

            set
            {
                _spanishCheckVisibility = value;
                OnPropertyChanged();
            }
        }

        #endregion


        #region Notifications

        private NotificationApiClient? _notificationApiClient;

        private NotificationSignalRService? _notificationSignalRService;

        private readonly ObservableCollection<NotificationDto>
            _notifications =
                new ObservableCollection<NotificationDto>();

        public ReadOnlyObservableCollection<NotificationDto>
            Notifications
        { get; }

        private int _unreadNotificationsCount;

        public int UnreadNotificationsCount
        {
            get => _unreadNotificationsCount;

            private set
            {
                if (SetProperty(
                    ref _unreadNotificationsCount,
                    value))
                {
                    OnPropertyChanged(
                        nameof(UnreadNotificationsVisibility));
                }
            }
        }

        public Visibility UnreadNotificationsVisibility =>
            UnreadNotificationsCount > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        private Func<
            NotificationDto,
            Task>? _showNotificationToastAsync;

        #endregion


        public MainWindowViewModel()
        {
            // Visibility logic:
            // Role 1: See everything.
            // Role 3: See everything except Admin.
            // Others: Just see Home.

            string currentRole = SessionManager.Role;

            // IsAdmin: Only if role is 1
            IsAdmin = currentRole == "1";
            IsBookingVisible = currentRole == "6" || currentRole == "1" || currentRole == "3";
            IsDriverVisible = currentRole == "2" || currentRole == "1" || currentRole == "3";

            // IsGeneralVisible: If it is role 1 OR it is role 3
            IsGeneralVisible =
                currentRole == "1" ||
                currentRole == "3";

            //IsAdmin = (SessionManager.Role.Equals("1", StringComparison.OrdinalIgnoreCase)) ? true : false;
            //LoadUsers();

            ChangeLanguageCommand =
                new RelayCommand<string>(ChangeLanguage);

            LogoutCommand =
                new RelayCommand(Logout);

            OpenNotificationCenterCommand =
                new RelayCommand(
                    OpenNotificationCenter);

            Notifications =
                new ReadOnlyObservableCollection<NotificationDto>(
                    _notifications);

            Languages =
                new ObservableCollection<LanguageOption>
                {
                    new LanguageOption
                    {
                        LanguageCode = "en",
                        DisplayName = MenuEnglishLanguage,
                        FlagEmoji = "🇺🇸"
                    },

                    new LanguageOption
                    {
                        LanguageCode = "es",
                        DisplayName = MenuSpanishLanguage,
                        FlagEmoji = "🇲🇽"
                    },

                    new LanguageOption
                    {
                        LanguageCode = "fr",
                        DisplayName = "Français",
                        FlagEmoji = "🇫🇷"
                    },
                };

            // Select saved language
            var currentLang =
                Properties.Settings.Default.Language ?? "en";

            var match =
                Languages.FirstOrDefault(
                    x => x.LanguageCode == currentLang)
                ?? Languages.First();

            match.IsSelected = true;

            SelectedLanguage = match;


            if (SelectedLanguage.LanguageCode.Equals("en"))
            {
                EnglishCheckVisibility =
                    SelectedLanguage.CheckVisibility;

                SpanishCheckVisibility =
                    Visibility.Collapsed;
            }
            else if (SelectedLanguage.LanguageCode.Equals("es"))
            {
                SpanishCheckVisibility =
                    SelectedLanguage.CheckVisibility;

                EnglishCheckVisibility =
                    System.Windows.Visibility.Collapsed;
            }
        }


        #region Notifications

        public void InitializeNotifications(
            NotificationApiClient notificationApiClient,
            NotificationSignalRService notificationSignalRService,
            Func<NotificationDto, Task> showNotificationToastAsync)
        {
            _notificationApiClient =
                notificationApiClient;

            _notificationSignalRService =
                notificationSignalRService;

            _showNotificationToastAsync =
                showNotificationToastAsync;
        }

        public async Task StartNotificationsAsync()
        {
            if (_notificationApiClient == null ||
                _notificationSignalRService == null)
            {
                return;
            }

            try
            {
                await RefreshNotificationsAsync();

                await _notificationSignalRService.StartAsync();
            }
            catch
            {
                // Notifications must not prevent
                // the application from starting.
            }
        }

        public async Task RefreshNotificationsAsync()
        {
            if (_notificationApiClient == null)
                return;

            try
            {
                var notifications =
                    await _notificationApiClient
                        .GetNotificationsAsync();

                Application.Current.Dispatcher.Invoke(
                    () =>
                    {
                        _notifications.Clear();

                        foreach (var notification in notifications)
                        {
                            _notifications.Add(notification);
                        }

                        UpdateUnreadCount();
                    });
            }
            catch
            {
                // Ignore notification loading errors.
            }
        }

        public async Task HandleNotificationReceivedAsync(
            NotificationDto notification)
        {
            if (notification == null)
                return;

            var existing =
                _notifications.FirstOrDefault(
                    x => x.Id == notification.Id);

            if (existing != null)
            {
                _notifications.Remove(existing);
            }

            _notifications.Insert(
                0,
                notification);

            UpdateUnreadCount();

            if (_showNotificationToastAsync != null)
            {
                await _showNotificationToastAsync(
                    notification);
            }
        }

        public void HandleNotificationViewed(
            Guid notificationRecipientId)
        {
            UpdateUnreadCount();
        }

        public void HandleNotificationAcknowledged(
            Guid notificationRecipientId)
        {
            UpdateUnreadCount();
        }

        private void UpdateUnreadCount()
        {
            UnreadNotificationsCount =
                _notifications
                    .SelectMany(
                        n => n.Recipients)
                    .Count(
                        r =>
                            !r.IsViewed &&
                            !r.IsAcknowledged);
        }

        private void OpenNotificationCenter()
        {
            if (_notifications.Count == 0)
            {
                MessageBox.Show(
                    "There are no notifications.",
                    "Notifications",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var message =
                string.Join(
                    Environment.NewLine +
                    Environment.NewLine,
                    _notifications.Select(
                        n =>
                            $"{n.Title}\n{n.Message}"));

            MessageBox.Show(
                message,
                "Notification Center",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        #endregion


        /*private async void LoadUsers()
        {
            UserService userService = new UserService();
            var users = await userService.GetUsersAsync();
            List<User> AllUsers = new List<User>(users);
            foreach (var user in users)
            {
                AllUsers.Add(user);
            }
            User CurrentUser = AllUsers.FirstOrDefault(u => u.Id == int.Parse(SessionManager.UserId));
            IsAdmin = (CurrentUser.RoleId == 1) ? true : false;
            //IsAdmin = CurrentUser?.Role?.RoleName?.Equals("Admin", StringComparison.OrdinalIgnoreCase) ?? false;
        }*/

        private void ChangeLanguage(
            string languageCode)
        {
            if (LocalizationService.Instance.CurrentLanguage !=
                languageCode)
            {
                LocalizationService.Instance.LoadLanguage(
                    languageCode);

                foreach (var lang in Languages)
                {
                    lang.IsSelected =
                        lang.LanguageCode == languageCode;

                    if (lang.LanguageCode.Equals("en"))
                    {
                        EnglishCheckVisibility =
                            lang.CheckVisibility;
                    }
                    else if (lang.LanguageCode.Equals("es"))
                    {
                        SpanishCheckVisibility =
                            lang.CheckVisibility;
                    }
                }
            }

            //LocalizationService.Instance.LoadLanguage(languageCode);
        }

        private void Logout()
        {
            Application.Current.Dispatcher.Invoke(
                () =>
                {
                    SessionManager.Clear();
                    var loginWindow =
                        new LoginWindow();

                    loginWindow.Show();

                    foreach (Window window
                        in Application.Current.Windows)
                    {
                        if (window is MainWindow)
                        {
                            window.Close();
                            break;
                        }
                    }
                });
        }
    }
}