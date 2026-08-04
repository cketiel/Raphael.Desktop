using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Raphael.Desktop.ViewModels
{
    public class AddEditUserViewModel : BaseViewModel
    {
        private User _user;
        private bool _isEditMode;
        private IUserService _userService;   
        private string _password;

        private IIntegratorService _integratorService;
        private IProviderService _providerService;

        public ObservableCollection<Integrator> AvailableIntegrators { get; set; }
        public ObservableCollection<Provider> AvailableProviders { get; set; }

        public bool IsCreateMode { get; }

        public User User
        {
            get => _user;
            set => SetProperty(ref _user, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public ObservableCollection<Role> AvailableRoles { get; set; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public AddEditUserViewModel(User user, List<Role> availableRoles, List<Integrator> integrators, List<Provider> providers)
        {
            _userService = new UserService();
            AvailableRoles = new ObservableCollection<Role>(availableRoles);
            AvailableIntegrators = new ObservableCollection<Integrator>(integrators);
            AvailableProviders = new ObservableCollection<Provider>(providers);

            if (user == null)
            {
                User = new User { IsActive = true }; 
                _isEditMode = false;
                IsCreateMode = true;
            }
            else
            {               
                User = new User
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Username = user.Username,
                    PasswordHash = user.PasswordHash, 
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber,
                    Address = user.Address,
                    DriverLicense = user.DriverLicense,
                    IsActive = user.IsActive,
                    RoleId = user.RoleId,
                    IntegratorId = user.IntegratorId, 
                    ProviderId = user.ProviderId
                };
                _isEditMode = true;
                IsCreateMode = false;
            }

            SaveCommand = new RelayCommandObject(Save, CanSave);
            CancelCommand = new RelayCommandObject(Cancel);
        }

        private bool CanSave(object parameter)
        {
            // Agrega las validaciones que necesites
            return !string.IsNullOrWhiteSpace(User.FullName) &&
                   !string.IsNullOrWhiteSpace(User.Username) &&
                   User.RoleId > 0;
        }

        private async void Save(object parameter)
        {
            try
            {
                if (!string.IsNullOrEmpty(Password))
                {
                    User.PasswordHash = Password; 
                }

                if (_isEditMode)
                {
                    var userDto = new UserUpdateDto
                    {
                        Id = User.Id,
                        FullName = User.FullName,
                        Username = User.Username,
                        Email = User.Email,
                        PhoneNumber = User.PhoneNumber,
                        Address = User.Address,
                        IsActive = User.IsActive,
                        RoleId = User.RoleId,
                        IntegratorId = User.IntegratorId,
                        ProviderId = User.ProviderId
                    };                    
                    await _userService.UpdateUserAsync(userDto);
                }
                else
                {
                    var userDto = new UserCreateDto
                    {
                        FullName = User.FullName,
                        Username = User.Username,
                        Password = this.Password, 
                        Email = User.Email,
                        PhoneNumber = User.PhoneNumber,
                        Address = User.Address,
                        IsActive = User.IsActive,
                        RoleId = User.RoleId,
                        IntegratorId = User.IntegratorId,
                        ProviderId = User.ProviderId
                    };
                    await _userService.AddUserAsync(userDto);
                }
                CloseWindow(parameter as Window, true);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error saving user: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel(object parameter)
        {
            CloseWindow(parameter as Window, false);
        }

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