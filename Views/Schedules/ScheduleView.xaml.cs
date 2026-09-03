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
using Raphael.Desktop.Models; 
using GMap.NET;

namespace Raphael.Desktop.Views.Schedules
{
    /// <summary>
    /// Lógica de interacción para ScheduleView.xaml
    /// </summary>
    public partial class ScheduleView : UserControl
    {
        public ScheduleView()
        {
            InitializeComponent();          

            this.DataContextChanged += ScheduleView_DataContextChanged;
            this.Unloaded += ScheduleView_Unloaded;
            this.Loaded += ScheduleView_Loaded;
          
            try
            {
                MapView.MapProvider = GMap.NET.MapProviders.GoogleMapProvider.Instance;
                GMap.NET.GMaps.Instance.Mode = GMap.NET.AccessMode.ServerAndCache;
                MapView.DragButton = MouseButton.Left;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error initializing GMap.NET: " + ex.Message);
            }
        }

        private async void ScheduleView_Loaded(object sender, RoutedEventArgs e)
        {            
            if (DataContext is SchedulesViewModel viewModel)
            {
                // Re-attached here, not only on DataContextChanged: unloading detaches
                // this control's own handlers, and unloading happens on every tab switch,
                // not just on a close. Without this the grid would stop scrolling to a
                // cancelled row after the dispatcher visited any other tab once.
                // Idempotent, so returning to the tab repeatedly cannot stack handlers.
                viewModel.ScrollUnscheduledTripIntoViewRequest -= OnScrollUnscheduledTripIntoView;
                viewModel.ScrollUnscheduledTripIntoViewRequest += OnScrollUnscheduledTripIntoView;

                if (!viewModel.IsInitialized)
                {
                    await viewModel.InitializeAsync();
                }
                viewModel.TriggerZoomToFit();
            }

        }

        private void ScheduleView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old ViewModel if it changes
            if (e.OldValue is SchedulesViewModel oldVm)
            {
                oldVm.ZoomAndCenterRequest -= OnZoomAndCenterRequest;
                oldVm.ScrollUnscheduledTripIntoViewRequest -= OnScrollUnscheduledTripIntoView;
            }

            // Subscribe to the new ViewModel
            if (e.NewValue is SchedulesViewModel newVm)
            {
                newVm.ZoomAndCenterRequest += OnZoomAndCenterRequest;
                newVm.ScrollUnscheduledTripIntoViewRequest += OnScrollUnscheduledTripIntoView;
            }
        }

        // Alternative subscription method if you always create the VM in the constructor
        private void SubscribeToViewModelEvents(SchedulesViewModel viewModel)
        {
            if (viewModel != null)
            {
                viewModel.ZoomAndCenterRequest += OnZoomAndCenterRequest;
                viewModel.ScrollUnscheduledTripIntoViewRequest += OnScrollUnscheduledTripIntoView;
            }
        }

        /// <summary>
        /// Brings a row of the open-trips grid into view so the dispatcher sees what is
        /// happening to it.
        /// </summary>
        /// <remarks>
        /// ⚠️ Scrolls without selecting. Moving the selection would clear the map points
        /// the dispatcher had up and change which trip the schedule button acts on —
        /// routing reads the selection, not the row that was clicked. <c>ScrollIntoView</c>
        /// leaves keyboard focus where it was; <c>Focus()</c> would not.
        /// </remarks>
        private void OnScrollUnscheduledTripIntoView(object sender, UnscheduledTripEventArgs e)
        {
            if (e?.Trip is null)
                return;

            UnscheduledTripsGrid?.ScrollIntoView(e.Trip);
        }

        /// <summary>
        /// Gives the row under the pointer the selection before the click is delivered to
        /// whatever is inside it.
        /// </summary>
        /// <remarks>
        /// The action buttons live in the row and act on the row they are in — they carry the
        /// trip as their CommandParameter and never needed the selection. But the grid spends
        /// the first click of an unselected row moving selection and focus to it, so the
        /// dispatcher had to press twice: once to point at the trip, once to do the thing.
        /// Selecting here, on the preview pass, means one press both points and acts.
        ///
        /// The event is deliberately not marked handled: the click carries on to the button.
        /// </remarks>
        private void UnscheduledTripsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;

            while (element != null && element is not DataGridRow)
            {
                element = element is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            if (element is DataGridRow row && !row.IsSelected)
            {
                row.IsSelected = true;
            }
        }

        // The event handler that calls the map method
        private void OnZoomAndCenterRequest(object sender, ZoomAndCenterEventArgs e)
        {
            if (e.BoundingBox != null && DataContext is SchedulesViewModel viewModel)
            {
                MapView.SetZoomToFitRect(e.BoundingBox);

                // SetZoomToFitRect can settle the zoom and the centre without either raising a
                // change the layer sees, so the markers are told explicitly. This used to rebuild
                // the entire Schedules collection to achieve the same thing.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    viewModel.InvalidateMapMarkers();

                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void ScheduleView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Cleanup to prevent memory leaks
            if (this.DataContext is SchedulesViewModel vm)
            {
                vm.ZoomAndCenterRequest -= OnZoomAndCenterRequest;
                vm.ScrollUnscheduledTripIntoViewRequest -= OnScrollUnscheduledTripIntoView;

                // ⚠️ The inbox subscription is NOT released here. This fires on a tab
                // switch as well as on a close, and a panel that stopped hearing about
                // cancellations would start offering cancelled trips as routable again.
                // MainWindow lets go of it when the tab is really closed.
                vm.Cleanup();
            }
            this.DataContextChanged -= ScheduleView_DataContextChanged;
            this.Unloaded -= ScheduleView_Unloaded;
            
        }

        private void ScheduleMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // We verify that we have all the necessary objects
            if (sender is FrameworkElement element &&
                element.DataContext is DTOs.ScheduleDto clickedSchedule &&
                this.DataContext is ViewModels.SchedulesViewModel viewModel)
            {
                

                if (viewModel.SelectedSchedule == clickedSchedule)
                {
                    // CASE 1: The bookmark that was already selected was clicked.
                    // We deselect it by assigning null.
                    viewModel.SelectedSchedule = null;
                }
                else
                {
                    // CASE 2: A different bookmark was clicked.
                    // We select it as before.
                    viewModel.SelectedSchedule = clickedSchedule;
                }

                // We mark the event as handled to prevent the click from moving the map.
                e.Handled = true;
            }
        }

        private void SetNormalMap_Click(object sender, RoutedEventArgs e)
        {
            MapView.MapProvider = GMap.NET.MapProviders.GoogleMapProvider.Instance;
        }

        private void SetHybridMap_Click(object sender, RoutedEventArgs e)
        {           
            MapView.MapProvider = GMap.NET.MapProviders.GoogleHybridMapProvider.Instance;
        }

        private void SetSatelliteMap_Click(object sender, RoutedEventArgs e)
        {
            MapView.MapProvider = GMap.NET.MapProviders.GoogleSatelliteMapProvider.Instance;
        }
    }
}
