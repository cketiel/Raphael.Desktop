
using System.Windows.Controls;

using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Data.Scheduling.Vehicles
{
    /// <summary>
    /// Lógica de interacción para VehiclesView.xaml
    /// </summary>
    public partial class VehiclesView : UserControl
    {
        public VehiclesView()
        {
            InitializeComponent();
            DataContext = new VehiclesViewModel();
        }
    }
}
