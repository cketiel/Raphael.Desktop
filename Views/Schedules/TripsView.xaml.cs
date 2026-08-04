using Raphael.Desktop.DTOs;
using Raphael.Desktop.ViewModels;
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

namespace Raphael.Desktop.Views.Schedules
{
    /// <summary>
    /// Lógica de interacción para TripsView.xaml
    /// </summary>
    public partial class TripsView : UserControl
    {
        public TripsView()
        {
            InitializeComponent();
            DataContext = new ViewModels.TripsViewModel();
        }
        private void RunComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // We get the ComboBox that fired the event
            var comboBox = sender as ComboBox;
            if (comboBox == null) return;

            // If the control is not loaded or if there are no elements removed,
            // means it is the initial WPF binding and not a user action.
            if (!comboBox.IsLoaded || e.RemovedItems.Count == 0) return;

            // We get the row object (TripReadDto)
            var trip = comboBox.DataContext as TripReadDto;

            // We get the main ViewModel
            var viewModel = this.DataContext as TripsViewModel;

            if (trip != null && viewModel != null)
            {
                // We execute the ViewModel command manually
                if (viewModel.UpdateTripRunCommand.CanExecute(trip))
                {
                    viewModel.UpdateTripRunCommand.Execute(trip);
                }
            }
        }
    }
}
