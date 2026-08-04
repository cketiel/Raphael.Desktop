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
using Raphael.Desktop.Models;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Employees
{
    public partial class AddEditUserView : Window
    {
        public AddEditUserView(User user, List<Role> availableRoles, List<Integrator> integrators, List<Provider> providers)
        {
            InitializeComponent();
            DataContext = new AddEditUserViewModel(user, availableRoles, integrators, providers);
        }

        // Event handler to update the ViewModel's Password property
        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is AddEditUserViewModel viewModel)
            {
                viewModel.Password = ((PasswordBox)sender).Password;
            }
        }
    }
}
