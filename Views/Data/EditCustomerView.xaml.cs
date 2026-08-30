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
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Maps;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Data
{
    /// <summary>
    /// Lógica de interacción para EditCustomerView.xaml
    /// </summary>
    public partial class EditCustomerView : Window
    {
        //public EditCustomerViewModel ViewModel { get; set; }
        public EditCustomerViewModel ViewModel => DataContext as EditCustomerViewModel;
        private bool _isUpdatingFromHtml = false;

        private readonly IMapsUsageApiService _mapsUsageApiService = new MapsUsageApiService();

        /// <summary>
        /// Answers what the map page cannot: the address at a dragged pin, and the details of
        /// a chosen place. Both come from our own database whenever anyone has asked before.
        /// </summary>
        private readonly IRoutingApiService _routingApiService = new RoutingApiService();

        public EditCustomerView()
        {
            InitializeComponent();
            /*ViewModel = new EditCustomerViewModel(new Models.Customer());
            DataContext = ViewModel;*/
        }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // By clicking Save, we set the dialog result to 'true'
            // and the window will close automatically because IsCancel=true on the other button takes care of the 'false' case.          
            this.DialogResult = true;
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

                        // Addresses and places, from the cache when we have them.
                        if (MapWebViewHost.TryHandleLookup(json, MapWebView, _routingApiService)) return;

                        dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                       
                        if (data.type == "autocomplete")
                        {
                            var result = data.result;

                            if (result is null) return;

                            _isUpdatingFromHtml = true;

                            AddressTextBox.Text = result.address;
                            //Address.Text = result.address;
                            CityTextBox.Text = result.city;
                            StateTextBox.Text = result.state;
                            ZipTextBox.Text = result.zip;

                            ViewModel.CustomerToEdit.Latitude = result.lat;
                            ViewModel.CustomerToEdit.Longitude = result.lng;

                            _isUpdatingFromHtml = false;

                            //ShowLocationOnMap((double)result.lat, (double)result.lng);
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

            double latitude = ViewModel?.CustomerToEdit.Latitude ?? 25.77427;
            double longitude = ViewModel?.CustomerToEdit.Longitude ?? -80.19366;

            // Served from the maps virtual host rather than pasted into the control as a string:
            // that is what gives the page an origin, and what lets the Google key be restricted.
            MapWebViewHost.Navigate(MapWebView, "basemap.html", ("lat", latitude), ("lng", longitude));
        }

        private void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromHtml) return;

            var text = AddressTextBox.Text;
            string js = $"document.getElementById('pickup').value = `{EscapeJs(text)}`;";
            MapWebView.ExecuteScriptAsync(js);
        }

        // Escape text for safe use in JavaScript
        private string EscapeJs(string input)
        {
            return input.Replace("\\", "\\\\").Replace("`", "\\`").Replace("\n", "").Replace("\r", "");
        }

        private void MapWebView_CoreWebView2InitializationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            // ⚠️ Deliberately does not navigate. This fires from inside EnsureCoreWebView2Async,
            // before the virtual host has been mapped, so a navigation here would land on an
            // error page and be corrected a moment later by WebView_Loaded — a visible flicker
            // for no reason. WebView_Loaded owns the loading of this map.
            if (!e.IsSuccess)
            {
                MessageBox.Show("Failed to initialize WebView2.");
            }
        }
    } // end class
}
