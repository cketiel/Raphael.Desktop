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

        /// <summary>RE-093: minutes typed by a person; Enter or leaving the box saves them.</summary>
        private async void Time_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox { DataContext: Models.SpaceType type, Tag: string field } box || DataContext is not SpaceTypesViewModel vm) return;
            await vm.ChangeTimeAsync(type, field == "LoadTime", box.Text);
            box.Text = (field == "LoadTime" ? type.LoadTime : type.UnloadTime).ToString("0.#");
        }

        private void Time_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Keyboard.ClearFocus();
        }

        /// <summary>A class chosen by a person: the ViewModel confirms and saves it.</summary>
        private async void Class_DropDownClosed(object sender, System.EventArgs e)
        {
            if (sender is ComboBox { DataContext: Models.SpaceType type, SelectedItem: DTOs.CapacityClassDto to } && DataContext is SpaceTypesViewModel vm && to.Id != type.ClassId)
                await vm.ChangeClassAsync(type, to);
        }
    }
}
