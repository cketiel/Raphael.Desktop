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
using Raphael.Desktop.Services;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Data.Scheduling
{
    /// <summary>
    /// Lógica de interacción para RunsView.xaml
    /// </summary>
    public partial class RunsView : UserControl
    {
        public RunsViewModel ViewModel => DataContext as RunsViewModel;
        public RunsView()
        {
            InitializeComponent();
            this.DataContext = new RunsViewModel(new RunService());
        }
    }
}
