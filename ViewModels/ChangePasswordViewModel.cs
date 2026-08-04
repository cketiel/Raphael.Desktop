using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System.Windows;
using System.Windows.Input;

namespace Raphael.Desktop.ViewModels
{
    public class ChangePasswordViewModel : BaseViewModel
    {
        private readonly int _userId;
        private IUserService _userService;

        private string _currentPassword;
        private string _newPassword;
        private string _confirmPassword;

        public string CurrentPassword { get => _currentPassword; set => SetProperty(ref _currentPassword, value); }
        public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }
        public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public ChangePasswordViewModel(int userId)
        {
            _userId = userId;
            _userService = new UserService();
            SaveCommand = new RelayCommandObject(Save, CanSave);
            CancelCommand = new RelayCommandObject(Cancel);
        }

        private bool CanSave(object parameter)
        {
            return !string.IsNullOrWhiteSpace(CurrentPassword) &&
                   !string.IsNullOrWhiteSpace(NewPassword) &&
                   !string.IsNullOrWhiteSpace(ConfirmPassword);
        }

        private async void Save(object parameter)
        {
            if (NewPassword != ConfirmPassword)
            {
                MessageBox.Show("The new password and confirmation password do not match.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dto = new ChangePasswordDto
            {
                UserId = _userId,
                CurrentPassword = this.CurrentPassword,
                NewPassword = this.NewPassword,
                ConfirmPassword = this.ConfirmPassword
            };

            try
            {
                await _userService.ChangePasswordAsync(dto);
                MessageBox.Show("Password changed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                CloseWindow(parameter as Window, true);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error changing password: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel(object parameter) => CloseWindow(parameter as Window, false);

        private void CloseWindow(Window window, bool dialogResult)
        {
            if (window != null)
            {
                window.DialogResult = dialogResult;
                window.Close();
            }
        }
    }
}