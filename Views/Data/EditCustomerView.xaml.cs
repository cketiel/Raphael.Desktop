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
                await MapWebView.EnsureCoreWebView2Async(); //MapWebView.DefaultBackgroundColor = System.Drawing.Color.Red;
                LoadMap();
                // Subscribe to message from JavaScript
                MapWebView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    try
                    {
                        var json = args.WebMessageAsJson;
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

            string apiKey = App.Configuration["GoogleMaps:ApiKey"];

            double latitude = ViewModel?.CustomerToEdit.Latitude ?? 25.77427;
            double longitude = ViewModel?.CustomerToEdit.Longitude ?? -80.19366;

            
            string htmlPath = File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "basemap.html"));
                htmlPath = htmlPath.Replace("{{API_KEY}}", apiKey);

                htmlPath = htmlPath.Replace("{ORIGIN_LAT}", latitude.ToString(CultureInfo.InvariantCulture))
                                   .Replace("{ORIGIN_LNG}", longitude.ToString(CultureInfo.InvariantCulture));


                MapWebView.NavigateToString(htmlPath);
                //MapWebView.NavigateToString("<html><body><h1>Prueba WebView2</h1></body></html>");
                //MapWebView.CoreWebView2.OpenDevToolsWindow();
            
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
            if (e.IsSuccess)
            {
                LoadMap();
            }
            else
            {
                MessageBox.Show("Failed to initialize WebView2.");
            }
        }
    } // end class
}
