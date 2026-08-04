using System.Windows;
using System.Windows.Controls;
using Raphael.Desktop.Services;
using System.Threading.Tasks;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            UpdateLoginButtonState();
        }

        private void UpdateLoginButtonState()
        {
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Visibility == Visibility.Visible
                          ? PasswordBox.Password
                          : PasswordVisibleBox.Text;

            LoginButton.IsEnabled = !string.IsNullOrEmpty(username) &&
                                  !string.IsNullOrEmpty(password);
        }

        private void Credentials_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateLoginButtonState();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateLoginButtonState();
        }

        private void TogglePasswordVisibility_Checked(object sender, RoutedEventArgs e)
        {
            PasswordVisibleBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordVisibleBox.Visibility = Visibility.Visible;
        }

        private void TogglePasswordVisibility_Unchecked(object sender, RoutedEventArgs e)
        {
            PasswordBox.Password = PasswordVisibleBox.Text;
            PasswordVisibleBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset UI state
            ErrorMessageText.Visibility = Visibility.Collapsed;
            LoginProgress.Visibility = Visibility.Visible;
            LoginProgress.IsIndeterminate = true;
            LoginButton.IsEnabled = false;

            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Visibility == Visibility.Visible
                         ? PasswordBox.Password
                         : PasswordVisibleBox.Text;

            try
            {
                using (var authService = new AuthService())
                {
                    var result = await authService.LoginAsync(username, password);

                    if (result != null && result.IsSuccess)
                    {
                        HandleSuccessfulLogin(username, result);
                    }
                    else
                    {
                        ShowError(result?.Message ?? "Login failed. Please try again.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                ShowError("An error occurred during login. Please try again.");
                System.Diagnostics.Debug.WriteLine($"Login error: {ex}");
            }
            finally
            {
                LoginProgress.Visibility = Visibility.Collapsed;
                UpdateLoginButtonState();
            }
        }

        private void HandleSuccessfulLogin(string username, LoginResponse result)
        {
            SessionManager.Token = result.Token; 
            SessionManager.Username = username;
            SessionManager.UserId = result.UserId;
            SessionManager.Role = result.Role;
            //StorageHelper.SaveUsername(username);
            SessionManager.IntegratorId = result.IntegratorId; 
            SessionManager.ProviderId = result.ProviderId;

            var mainWindow = new MainWindow();
            mainWindow.WindowState = WindowState.Maximized;
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            this.Close();
        }

        private void ShowError(string message)
        {
            ErrorMessageText.Text = message;
            ErrorMessageText.Visibility = Visibility.Visible;

            var animation = new System.Windows.Media.Animation.ThicknessAnimation(
                new Thickness(-5, 0, 0, 0),
                new Thickness(5, 0, 0, 0),
                TimeSpan.FromSeconds(0.1))
            {
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3)
            };

            LoginButton.BeginAnimation(MarginProperty, animation);
        }
    }
}