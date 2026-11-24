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

namespace Raphael.Desktop.Views.Data.Scheduling.Vehicles
{
    /// <summary>
    /// Interaction logic for SpaceTypesView.xaml
    /// </summary>
    public partial class SpaceTypesView : UserControl
    {
        public SpaceTypesView()
        {
            InitializeComponent();
            DataContext = new SpaceTypesViewModel();
        }

        /// <summary>A class chosen by a person: the ViewModel confirms and saves it.</summary>
        private async void Class_DropDownClosed(object sender, System.EventArgs e)
        {
            if (sender is ComboBox { DataContext: Models.SpaceType type, SelectedItem: DTOs.CapacityClassDto to } && DataContext is SpaceTypesViewModel vm && to.Id != type.ClassId)
                await vm.ChangeClassAsync(type, to);
        }
    }
}
