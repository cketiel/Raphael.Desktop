
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Raphael.Desktop.ViewModels;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// Lógica de interacción para DispatchView.xaml
    /// </summary>
    public partial class DispatchView : UserControl
    {
        public DispatchView()
        {
            InitializeComponent();
            DataContext = new DispatchViewModel();
            this.DataContextChanged += OnDataContextChanged;
            this.Unloaded += OnUnloaded;

            this.Loaded += (s, e) =>
            {
                try
                {
                    
                    OverviewMapView.MapProvider = GMap.NET.MapProviders.GoogleMapProvider.Instance;
                    GMap.NET.GMaps.Instance.Mode = GMap.NET.AccessMode.ServerAndCache;
                    OverviewMapView.DragButton = MouseButton.Left;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error initializing GMap.NET in DispatchView: " + ex.Message);
                }
            };
            
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Nos desuscribimos del ViewModel antiguo para evitar fugas de memoria.
            if (e.OldValue is DispatchViewModel oldVm)
            {
                oldVm.ZoomAndCenterRequest -= OnZoomAndCenterRequest;
            }

            // Nos suscribimos al nuevo ViewModel.
            if (e.NewValue is DispatchViewModel newVm)
            {
                newVm.ZoomAndCenterRequest += OnZoomAndCenterRequest;
            }
        }

        private void OnZoomAndCenterRequest(object sender, ZoomAndCenterEventArgs e)
        {
            if (e.BoundingBox != null)
            {
                // Le decimos al control del mapa que se ajuste al rectángulo proporcionado.
                OverviewMapView.SetZoomToFitRect(e.BoundingBox);
                // Forzamos un refresco visual para asegurar que los marcadores se dibujen correctamente.
                OverviewMapView.InvalidateVisual();
            }
        }

        // No olvides desuscribirte cuando el control se descargue.
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is DispatchViewModel vm)
            {
                vm.ZoomAndCenterRequest -= OnZoomAndCenterRequest;
            }
            this.DataContextChanged -= OnDataContextChanged;
            this.Unloaded -= OnUnloaded;
        }
    }
}
