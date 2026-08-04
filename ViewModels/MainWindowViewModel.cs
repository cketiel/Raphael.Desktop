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

        private bool _isGeneralVisible;
        public bool IsGeneralVisible
        {
            get => _isGeneralVisible;
            set => SetProperty(ref _isGeneralVisible, value);
        }
        public string LoggedUserName => "Hello, User"; // Example

        public ICommand ChangeLanguageCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand SelectionChangedCommand { get; }

        #region Translation

        // Main menu
        public string MainMenuHome => LocalizationService.Instance["Home"];
        public string MainMenuData => LocalizationService.Instance["Data"];
        public string MainMenuSchedules => LocalizationService.Instance["Schedules"];
        public string MainMenuDispatch => LocalizationService.Instance["Dispatch"];
        public string MainMenuReports => LocalizationService.Instance["Reports"];
        public string MainMenuAdmin => LocalizationService.Instance["Admin"];


        public string Settings => LocalizationService.Instance["Settings"];
        public string MenuChangeLanguage => LocalizationService.Instance["MenuChangeLanguage"];
        public string MenuEnglishLanguage => LocalizationService.Instance["MenuEnglishLanguage"];
        public string MenuSpanishLanguage => LocalizationService.Instance["MenuSpanishLanguage"];
        public string MenuLogout => LocalizationService.Instance["MenuLogout"];


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
                ChangeLanguage(value.LanguageCode);
            }
        }

        private Visibility _englishCheckVisibility = Visibility.Visible;
        public Visibility EnglishCheckVisibility
        {
            get => _englishCheckVisibility;
            set
            {
                _englishCheckVisibility = value;
                OnPropertyChanged();
            }
        }

        private Visibility _spanishCheckVisibility = Visibility.Collapsed;
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
        public MainWindowViewModel()
        {
            // Visibility logic:
            // Role 1: See everything.
            // Role 3: See everything except Admin.
            // Others: Just see Home.

            string currentRole = SessionManager.Role;

            // IsAdmin: Only if role is 1
            IsAdmin = currentRole == "1";

            // IsGeneralVisible: If it is role 1 OR it is role 3
            IsGeneralVisible = (currentRole == "1" || currentRole == "3");

            //IsAdmin = (SessionManager.Role.Equals("1", StringComparison.OrdinalIgnoreCase)) ? true : false;
            //LoadUsers();

            ChangeLanguageCommand = new RelayCommand<string>(ChangeLanguage);
            LogoutCommand = new RelayCommand(Logout);

            Languages = new ObservableCollection<LanguageOption>
            {
                new LanguageOption { LanguageCode = "en", DisplayName = MenuEnglishLanguage, FlagEmoji = "🇺🇸" },
                new LanguageOption { LanguageCode = "es", DisplayName = MenuSpanishLanguage, FlagEmoji = "🇲🇽" },
                new LanguageOption { LanguageCode = "fr", DisplayName = "Français", FlagEmoji = "🇫🇷" },
            };

            // Select saved language
            var currentLang = Properties.Settings.Default.Language ?? "en";
            var match = Languages.FirstOrDefault(x => x.LanguageCode == currentLang) ?? Languages.First();
            match.IsSelected = true;
            SelectedLanguage = match;

            
            if (SelectedLanguage.LanguageCode.Equals("en")) 
            {
                EnglishCheckVisibility = SelectedLanguage.CheckVisibility;
                SpanishCheckVisibility = Visibility.Collapsed;
            }else if (SelectedLanguage.LanguageCode.Equals("es"))
            {
                SpanishCheckVisibility = SelectedLanguage.CheckVisibility;
                EnglishCheckVisibility = System.Windows.Visibility.Collapsed;
            }
        }

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

        private void ChangeLanguage(string languageCode)
        {
            if (LocalizationService.Instance.CurrentLanguage != languageCode)
            {
                LocalizationService.Instance.LoadLanguage(languageCode);

                foreach (var lang in Languages)
                {
                    lang.IsSelected = lang.LanguageCode == languageCode;
                    if (lang.LanguageCode.Equals("en"))
                    {
                        EnglishCheckVisibility = lang.CheckVisibility;
                    }else if (lang.LanguageCode.Equals("es"))
                    {
                        SpanishCheckVisibility = lang.CheckVisibility;
                    }
                }
                    
            }
            //LocalizationService.Instance.LoadLanguage(languageCode);
        }

        private void Logout()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var loginWindow = new LoginWindow();
                loginWindow.Show();

                foreach (Window window in Application.Current.Windows)
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
