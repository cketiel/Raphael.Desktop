using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Employees
{
    /// <summary>
    /// Lógica de interacción para ChangePasswordView.xaml
    /// </summary>
    public partial class ChangePasswordView : Window
    {
        public ChangePasswordView(int userId)
        {
            InitializeComponent();
            var vm = new ChangePasswordViewModel(userId);
            DataContext = vm;

            // An admin resetting someone else's password does not know the current one.
            if (vm.IsReset)
            {
                Title = "Reset Password";
                CurrentPasswordLabel.Visibility = Visibility.Collapsed;
                CurrentPasswordBox.Visibility = Visibility.Collapsed;
            }
        }

        private void CurrentPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChangePasswordViewModel vm) vm.CurrentPassword = ((PasswordBox)sender).Password;
        }

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChangePasswordViewModel vm) vm.NewPassword = ((PasswordBox)sender).Password;
        }

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChangePasswordViewModel vm) vm.ConfirmPassword = ((PasswordBox)sender).Password;
        }
    }
}
