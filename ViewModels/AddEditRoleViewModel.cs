using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Models;
using System.Windows.Input;
using System.Windows;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    public class AddEditRoleViewModel : BaseViewModel
    {      
        private Role _role;
        private bool _isEditMode;
        private IRoleService _roleService;

        public Role Role
        {
            get => _role;
            set
            {
                _role = value;
                OnPropertyChanged();
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public AddEditRoleViewModel(Role role = null)
        {
            _roleService = new RoleService();
            if (role == null)
            {
                Role = new Role();
                _isEditMode = false;
            }
            else
            {
                Role = role;
                _isEditMode = true;
            }

            SaveCommand = new RelayCommandObject(Save, CanSave);
            CancelCommand = new RelayCommandObject(Cancel);
        }

        private bool CanSave(object parameter)
        {
            return !string.IsNullOrWhiteSpace(Role.RoleName);
        }

        private async void Save(object parameter)
        {
            if (_isEditMode)
            {
                await _roleService.UpdateRoleAsync(Role.Id, Role);
                /*var originalRole = Roles.FirstOrDefault(r => r.Id == Role.Id);
                if (originalRole != null)
                {
                    var index = _allRoles.IndexOf(originalRole);
                    _allRoles[index] = Role;                    
                }*/
            }
            else
            {              
                var addedRole = await _roleService.AddRoleAsync(Role);
                //_allRoles.Add(addedRole);
            }
            //reload grid
            CloseWindow(parameter as Window, true);
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
