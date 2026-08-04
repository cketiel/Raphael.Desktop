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
    /// <summary>
    /// Lógica de interacción para AddEditRoleView.xaml
    /// </summary>
    public partial class AddEditRoleView : Window
    {
        public AddEditRoleView(Role role = null)
        {
            InitializeComponent();
            DataContext = new AddEditRoleViewModel(role);
        }
    }
}
