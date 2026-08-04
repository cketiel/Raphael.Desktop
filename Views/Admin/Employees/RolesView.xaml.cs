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
using System.Windows.Navigation;
using System.Windows.Shapes;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Admin.Employees
{
    /// <summary>
    /// Lógica de interacción para RolesView.xaml
    /// </summary>
    public partial class RolesView : UserControl
    {
        public RolesView()
        {
            InitializeComponent();
            DataContext = new RolesViewModel();
        }
    }
}
