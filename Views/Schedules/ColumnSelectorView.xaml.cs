using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace Raphael.Desktop.Views.Schedules
{
    /// <summary>
    /// Lógica de interacción para ColumnSelectorView.xaml
    /// </summary>
    public partial class ColumnSelectorView : Window
    {
        public ColumnSelectorView()
        {
            InitializeComponent();
            //Action closeAction = null;
            //ObservableCollection<ColumnConfig> ColumnConfigurations = new();
            //DataContext = new ScheduleColumnSelectorViewModel(ColumnConfigurations, () => closeAction?.Invoke());
        }
    }
}
