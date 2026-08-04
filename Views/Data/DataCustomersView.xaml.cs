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

namespace Raphael.Desktop.Views.Data
{
    /// <summary>
    /// Lógica de interacción para DataCustomersView.xaml
    /// </summary>
    public partial class DataCustomersView : UserControl
    {
        public DataCustomersView()
        {
            InitializeComponent();
            DataContext = new CustomersViewModel();
        }
    }
}
