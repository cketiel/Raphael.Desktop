using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using DocumentFormat.OpenXml.InkML;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Admin.Employees;

namespace Raphael.Desktop.ViewModels
{
    public class RolesViewModel : BaseViewModel
    {
        #region Translation

        public string AddRoleToolTip => LocalizationService.Instance["AddRoleToolTip"];
        public string EditRoleToolTip => LocalizationService.Instance["EditRoleToolTip"];
        public string DeleteRoleToolTip => LocalizationService.Instance["DeleteRoleToolTip"];

        public string ColumnHeaderRoleName => LocalizationService.Instance["ColumnHeaderRoleName"];
        public string ColumnHeaderDescription => LocalizationService.Instance["ColumnHeaderDescription"];

        public string ErrorTitle => LocalizationService.Instance["ErrorTitle"];
        public string ConfirmDeleteRoleText => LocalizationService.Instance["ConfirmDeleteRoleText"];
        public string ConfirmDeleteTitle => LocalizationService.Instance["ConfirmDeleteTitle"];

        #endregion

        private ObservableCollection<Role> _roles;
        private Role _selectedRole;

        public ObservableCollection<Role> Roles
        {
            get => _roles;
            set => SetProperty(ref _roles, value);
        }
        public Role SelectedRole
        {
            get => _selectedRole;
            set => SetProperty(ref _selectedRole, value);
        }

        private IRoleService _roleService;

        public ICommand AddRoleCommand { get; }
        public ICommand EditRoleCommand { get; }
        public ICommand DeleteRoleCommand { get; }
        public RolesViewModel()
        {
            Roles = new ObservableCollection<Role>();
            AddRoleCommand = new RelayCommandObject(AddRole);
            EditRoleCommand = new RelayCommandObject(EditRole, CanEditOrDeleteRole);
            DeleteRoleCommand = new RelayCommandObject(DeleteRole, CanEditOrDeleteRole);
            // Load initial roles (this could be from a service or database)
            LoadRoles();
        }

        private async Task LoadRoles()
        {
            _roleService = new RoleService();
            try
            {
                var roles = await _roleService.GetRolesAsync();
                Roles = new ObservableCollection<Role>(roles);
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., show a message to the user)
                Console.WriteLine($"Error loading roles: {ex.Message}");
            }
        }

        private void AddRole(object parameter)
        {
            var addEditRoleView = new AddEditRoleView();
            var result = addEditRoleView.ShowDialog();

            if (result.HasValue && result.Value)
            {
                LoadRoles();
            }
        }

        private void EditRole(object parameter)
        {
            var addEditRoleView = new AddEditRoleView(SelectedRole);
            var result = addEditRoleView.ShowDialog();

            if (result.HasValue && result.Value)
            {
                LoadRoles();
            }
        }

        private bool CanEditOrDeleteRole(object parameter)
        {
            return SelectedRole != null;
        }

        private async void DeleteRole(object parameter )
        {
            Role roleToDelete = SelectedRole;
            if (roleToDelete == null) return;

            var confirmationText = string.Format(LocalizationService.Instance["ConfirmDeleteRoleText"], roleToDelete.RoleName); // ej: "Are you sure you want to delete the role '{0}'?"
            var confirmationTitle = LocalizationService.Instance["ConfirmDeleteTitle"]; // ej: "Confirm Delete"

            if (MessageBox.Show(confirmationText, confirmationTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await _roleService.DeleteRoleAsync(roleToDelete.Id);
                    //_allRoles.Remove(roleToDelete);
                    LoadRoles();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                            string.Format(LocalizationService.Instance["ErrorDeletingVehicle"], ex.Message), // ej: "Error deleting role: {0}"
                            ErrorTitle,
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    //MessageBox.Show($"Error deleting vehicle: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void DeleteRole2(object parameter)
        {
            var result = MessageBox.Show($"¿Está seguro de que desea eliminar el rol '{SelectedRole.RoleName}'?",
                                         "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                //_context.Roles.Remove(SelectedRole);
                //_context.SaveChanges();
                LoadRoles();
            }
        }
    }
}
