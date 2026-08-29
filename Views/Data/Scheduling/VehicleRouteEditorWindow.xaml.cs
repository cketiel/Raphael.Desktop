using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Maps;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Data.Scheduling
{
    /// <summary>
    /// Lógica de interacción para VehicleRouteEditorWindow.xaml
    /// </summary>
    public partial class VehicleRouteEditorWindow : Window
    {
        public VehicleRouteEditorViewModel ViewModel => DataContext as VehicleRouteEditorViewModel;
        private bool _isUpdatingFromHtml = false;

        private readonly IMapsUsageApiService _mapsUsageApiService = new MapsUsageApiService();

        public VehicleRouteEditorWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;

            // Suscribirse a los eventos del ViewModel para cerrar la ventana
            /*DataContextChanged += (s, e) =>
            {
                if (e.NewValue is VehicleRouteEditorViewModel vm)
                {
                    vm.RequestClose += (sender, dialogResult) =>
                    {
                        this.DialogResult = dialogResult;
                        this.Close();
                    };
                }
            };*/
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is VehicleRouteEditorViewModel oldVm)
            {
                oldVm.RequestClose -= OnRequestClose;
            }
            if (e.NewValue is VehicleRouteEditorViewModel newVm)
            {
                newVm.RequestClose += OnRequestClose;
            }
        }

        private void OnRequestClose(object sender, bool? dialogResult)
        {
            this.DialogResult = dialogResult;
            this.Close();
        }

        private async void WebView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await MapWebViewHost.InitializeAsync(MapWebView);
                LoadMap();
                // Subscribe to message from JavaScript
                MapWebView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    try
                    {
                        var json = args.WebMessageAsJson;

                        // What the page spent at Google itself. The server never sees these.
                        if (MapWebViewHost.TryForwardUsage(json, _mapsUsageApiService)) return;

                        dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                        if (data.type == "autocomplete")
                        {
                            var result = data.result;

                            if (result is null) return;

                            _isUpdatingFromHtml = true;

                            GarageTextBox.Text = result.address;
                            GarageLatitudeTextBox.Text = result.lat;
                            GarageLongitudeTextBox.Text = result.lng;

                            ViewModel.Route.GarageLatitude = result.lat;
                            ViewModel.Route.GarageLongitude = result.lng;

                            /*CityTextBox.Text = result.city;
                            StateTextBox.Text = result.state;
                            ZipTextBox.Text = result.zip;

                            ViewModel.CustomerToEdit.Latitude = result.lat;
                            ViewModel.CustomerToEdit.Longitude = result.lng;*/

                            _isUpdatingFromHtml = false;
                           
                        }

                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error receiving message: " + ex.Message);
                    }
                };

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing WebView2: {ex.Message}");
            }

        }

        private void LoadMap()
        {
            if (MapWebView.CoreWebView2 == null)
                return;

            double latitude = ViewModel?.Route?.GarageLatitude ?? 25.77427;
            double longitude = ViewModel?.Route?.GarageLongitude ?? -80.19366;

            MapWebViewHost.Navigate(MapWebView, "basemap.html", ("lat", latitude), ("lng", longitude));
        }

        private void GarageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromHtml) return;

            var text = GarageTextBox.Text;
            string js = $"document.getElementById('pickup').value = `{EscapeJs(text)}`;";
            MapWebView.ExecuteScriptAsync(js);
        }

        // Escape text for safe use in JavaScript
        private string EscapeJs(string input)
        {
            return input.Replace("\\", "\\\\").Replace("`", "\\`").Replace("\n", "").Replace("\r", "");
        }

    } // en partial class

    // Convertidor para cambiar el título de la ventana
    public class IdToTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var baseTitle = parameter as string ?? "Editor";
            if (value is int id && id > 0)
            {
                return $"Editar {baseTitle} (ID: {id})";
            }
            return $"Nuevo {baseTitle}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
