using System;
using System.IO;
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
using Raphael.Desktop.Models;
using System.Collections.ObjectModel;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using Raphael.Desktop.Services;
using System.Configuration;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Globalization;
using Raphael.Desktop.Helpers;
using System.Windows.Media.Animation;
using MaterialDesignColors;
using System.Windows.Media.Media3D;
using Raphael.Desktop.Exceptions;
using CsvHelper.Configuration;
using CsvHelper;
using Raphael.Desktop.Models.Csv;
using Microsoft.Win32;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;


using System.Collections.Generic;

using System.Linq;
using System.Diagnostics;
using Raphael.Desktop.DTOs;
using static MaterialDesignThemes.Wpf.Theme;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// Lógica de interacción para HomeView.xaml
    /// </summary>
    public partial class HomeView : UserControl
    {
        public HomeViewModel ViewModel { get; set; }
        private bool _isUpdatingFromHtml = false;

        // STATIC VARIABLE for the environment (shared throughout the App)
        private static CoreWebView2Environment _commonEnvironment;
        // Flag to avoid simultaneous calls on the same instance
        private bool _isInitializing = false;
        public HomeView()
        {
            InitializeComponent();
            ViewModel = new HomeViewModel();
            DataContext = ViewModel;
            ViewModel.OnTripSavedSuccess = ResetLayoutToInitialState;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            InitializeData();

            //SetupAutocompleteOverlay();

            // It doesn't work properly, it never loads the map.
            // For this reason, WebView_Loaded() was used.
            //MapaWebView.CoreWebView2InitializationCompleted += MapaWebView_CoreWebView2InitializationCompleted;
        }
        private async void InitializeData()
        {
            //ViewModel.LoadTripsFromApi();
            //InitializeWebView();

        }

        private void MapaWebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
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

        private async void WebView_Loaded(object sender, RoutedEventArgs e)
        {
            if (MapaWebView.CoreWebView2 != null || _isInitializing)
            {
                if (MapaWebView.CoreWebView2 != null) LoadMap();
                return;
            }

            _isInitializing = true;

            try
            {              

                if (_commonEnvironment == null)
                {
                    // This creates a route like: C:\Users\<TuUsuario>\AppData\Local\Raphael\WebView2
                    string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Raphael", "WebView2");

                    _commonEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                }

                // Wait for initialization with the shared environment
                await MapaWebView.EnsureCoreWebView2Async(_commonEnvironment);

                // Only subscribe to events if it's the first time (CoreWebView2 has just been created)
                ConfigureWebViewEvents();

                LoadMap();

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing WebView2: {ex.Message}");
            }
            finally
            {
                _isInitializing = false;
            }

        }

        private void ConfigureWebViewEvents()
        {
            // MapaWebView.WebMessageReceived += MapaWebView_WebMessageReceived;
            // Subscribe to message from JavaScript
            MapaWebView.CoreWebView2.WebMessageReceived += (s, args) =>
            {
                try
                {
                    var json = args.WebMessageAsJson;
                    dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                    if (data.type == "routeInfo")
                    {
                        string eta = data.eta;
                        string distance = data.distance;


                        // Show on Label
                        Dispatcher.Invoke(() =>
                        {
                            if (DataContext is HomeViewModel vm)
                            {
                                vm.Distance = distance; // double.Parse(distance); // meters
                                vm.ETA = eta; // double.Parse(eta); // seconds
                            }
                            //TripInfoLabel.Content = $"ETA: {eta} | Distance: {distance}";
                        });
                        // Here you can display the message
                        //MessageBox.Show($"ETA: {eta}\nDistance: {distance}", "Trip Info");
                    }
                    else if (data.type == "autocomplete")
                    {
                        var result = data.result;

                        if (result is null) return;

                        Dispatcher.Invoke(() =>
                        {
                            if (DataContext is HomeViewModel vm)
                            {
                                _isUpdatingFromHtml = true;
                                GooglePlacesInput.Text = result.address;
                                City.Text = result.city;
                                State.Text = result.state;
                                Zip.Text = result.zip;

                                if (vm.SelectedCustomer != null)
                                    vm.SelectedCustomer.Address = result.address;

                                // Guardar coordenadas en el ViewModel
                                vm.PickupLatitude = (double)result.lat;
                                vm.PickupLongitude = (double)result.lng;

                                vm.PickupCity = (string)result.city;
                                _isUpdatingFromHtml = false;
                            }
                        });

                        /*_isUpdatingFromHtml = true;

                        GooglePlacesInput.Text = result.address;
                        //Address.Text = result.address;
                        City.Text = result.city;
                        State.Text = result.state;
                        Zip.Text = result.zip;

                        _isUpdatingFromHtml = false;*/

                        //ShowLocationOnMap((double)result.lat, (double)result.lng);
                    }
                    else if (data.type == "pickup")
                    {
                        var result = data.result;

                        if (result is null) return;

                        Dispatcher.Invoke(() =>
                        {
                            if (DataContext is HomeViewModel vm)
                            {
                                _isUpdatingFromHtml = true;                              
                               vm.PickupAddress = (string)result.address;
                                // Guardar coordenadas en el ViewModel
                                vm.PickupLatitude = (double)result.lat;
                                vm.PickupLongitude = (double)result.lng;

                                vm.PickupCity = (string)result.city;
                                _isUpdatingFromHtml = false;
                            }
                        });
                      
                    }
                    else if (data.type == "dropoff")
                    {
                        if (data.dropoff_address is null) return;

                        Dispatcher.Invoke(() =>
                        {
                            if (DataContext is HomeViewModel vm)
                            {
                                _isUpdatingFromHtml = true;
                                //DropoffAddressTextBox.Text = data.dropoff_address;
                                vm.DropoffAddress = (string)data.dropoff_address;

                                // Guardar coordenadas en el ViewModel
                                vm.DropoffLatitude = (double)data.lat;
                                vm.DropoffLongitude = (double)data.lng;

                                vm.DropoffCity = (string)data.city ?? vm.DropoffCity;

                                _isUpdatingFromHtml = false;
                            }
                        });

                        /*_isUpdatingFromHtml = true;
                        DropoffAddressTextBox.Text = data.dropoff_address;
                        _isUpdatingFromHtml = false;*/
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error receiving message: " + ex.Message);
                }
            };

        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedTrip" && ViewModel.SelectedTrip != null)
            {
                LoadMap();
                ExpandLayoutForEditing();
            }
        }
        private void ExpandLayoutForEditing()
        {
            CustomerColumn.Width = new GridLength(3.2, GridUnitType.Star);
            BillingColumn.Width = new GridLength(2.4, GridUnitType.Star);
            MapColumn.Width = new GridLength(4.4, GridUnitType.Star);
            BillingPanel.Visibility = Visibility.Visible;
            ForDateCalendar.Visibility = Visibility.Visible;
            TripFilterPanel.Visibility = Visibility.Collapsed;
            TripTabs.Visibility = Visibility.Visible;

            // Ajustar el tamaño de las filas
            TopRow.Height = new GridLength(6.73, GridUnitType.Star);
            BottomRow.Height = new GridLength(3.27, GridUnitType.Star);

            // Mostrar el input de Dropoff en el mapa
            MapaWebView.ExecuteScriptAsync("showDropoff();");
        }
        private async void LoadMap()
        {
            var trip = ViewModel.SelectedTrip;

            if (MapaWebView.CoreWebView2 == null)
                return;

            string apiKey = App.Configuration["GoogleMaps:ApiKey"];

            if (trip == null || MapaWebView.CoreWebView2 == null)
            {
                string htmlPath = File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "basemap.html"));
                htmlPath = htmlPath.Replace("{{API_KEY}}", apiKey);

                htmlPath = htmlPath.Replace("{ORIGIN_LAT}", "25.77427")
                                   .Replace("{ORIGIN_LNG}", "-80.19366");


                MapaWebView.NavigateToString(htmlPath);
            }
            else
            {
                //MessageBox.Show($"La API Key es: {apiKey}");

                string htmlPath = File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "googlemap.html"));
                htmlPath = htmlPath.Replace("{{API_KEY}}", apiKey);

                htmlPath = htmlPath.Replace("{LAT_ORIGEN}", trip.PickupLatitude.ToString())
                                   .Replace("{LNG_ORIGEN}", trip.PickupLongitude.ToString())
                                   .Replace("{LAT_DESTINO}", trip.DropoffLatitude.ToString())
                                   .Replace("{ORIGEN}", trip.PickupAddress)
                                   //.Replace("{NOMBRE}", trip.PatientName)
                                   .Replace("{LNG_DESTINO}", trip.DropoffLongitude.ToString());

                MapaWebView.NavigateToString(htmlPath);

                // Esperar a que el mapa cargue y luego inyectar los valores en los inputs de arriba
                // Usamos un pequeño delay o NavigationCompleted para asegurar que los elementos DOM existan
                await Task.Delay(500);

                string fillInputsJs = $@"
                    document.getElementById('pickup').value = `{EscapeJs(trip.PickupAddress)}`;
                    document.getElementById('dropoff').value = `{EscapeJs(trip.DropoffAddress)}`;
                ";
                await MapaWebView.ExecuteScriptAsync(fillInputsJs);

                ShowETAInfo(trip);

            }




        }

        public void LoadMapExcample()
        {
            // Template HTML file path
            string templatePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "googlemap.html");

            // Read original HTML
            string htmlContent = File.ReadAllText(templatePath);

            // Read key from App.config
            string apiKey = ConfigurationManager.AppSettings["GoogleMapsApiKey"];

            // Replace {{API_KEY}} with the real one
            htmlContent = htmlContent.Replace("{{API_KEY}}", apiKey);

            // Crear archivo temporal con el HTML ya modificado
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "googlemap2.html");
            File.WriteAllText(tempPath, htmlContent);

            // Load into WebView2

            MapaWebView.CoreWebView2.Navigate(tempPath);
        }
        public async void ShowETAInfo(TripReadDto SelectedTrip) {

            GoogleMapsService googleMapsService = new GoogleMapsService();
            var routeInfo = await googleMapsService.GetRouteDurationsAndDistance(SelectedTrip.PickupLatitude, SelectedTrip.PickupLongitude, SelectedTrip.DropoffLatitude, SelectedTrip.DropoffLongitude);

            // Mostrar las duraciones y distancia en el Label
            Label etaLabel = new Label();
            etaLabel.Content = $"Duration without traffic: {routeInfo.noTraffic}\n" +
                              $"Duration with traffic: {routeInfo.withTraffic}\n" +
                              $"Duration (pessimistic): {routeInfo.pessimistic}\n" +
                              $"Duration (optimistic): {routeInfo.optimistic}\n" +
                              $"Distance: {routeInfo.distance}";

            Dispatcher.Invoke(() =>
            {
                //ETAInfoLabel.Content = etaLabel.Content;
            });
        }

        private void MapaWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                var data = JsonSerializer.Deserialize<RouteInfoMessage>(json);

                if (data?.Type == "routeInfo")
                {
                    MessageBox.Show($"ETA: {data.Eta}\nDistance: {data.Distance}", "Route Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error parsing message: {ex.Message}");
            }
        }

        public class RouteInfoMessage
        {
            public string Type { get; set; } = "";
            public string Eta { get; set; } = "";
            public string Distance { get; set; } = "";
            public string Pickup { get; set; } = "";
        }

        // testing
        private async void SetupAutocompleteOverlay()
        {

            string apiKey = App.Configuration["GoogleMaps:ApiKey"];


            string path = File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "autocomplete.html"));
            //string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "html", "autocomplete.html");

            path = path.Replace("{{API_KEY}}", apiKey);

            /*await AutocompleteOverlay.EnsureCoreWebView2Async();
            AutocompleteOverlay.NavigateToString(path);
            //AutocompleteOverlay.Source = new Uri(path); // AutocompleteOverlay.NavigateToString(htmlPath);
            AutocompleteOverlay.WebMessageReceived += AutocompleteOverlay_WebMessageReceived;*/
        }

        private void AutocompleteOverlay_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var json = e.TryGetWebMessageAsString();
            var result = JsonSerializer.Deserialize<MapAddressResult>(json ?? "");

            if (result is null) return;

            _isUpdatingFromHtml = true;

            GooglePlacesInput.Text = result.address;
            //Address.Text = result.address;
            City.Text = result.city;
            State.Text = result.state;
            Zip.Text = result.zip;

            _isUpdatingFromHtml = false;

            ShowLocationOnMap(result.lat, result.lng);
        }

        private async void ShowLocationOnMap(double lat, double lng)
        {
            string js = $"setMarkerAt({lat}, {lng});";
            await MapaWebView.ExecuteScriptAsync(js);
        }

        private class MapAddressResult
        {
            public string address { get; set; }
            public string city { get; set; }
            public string state { get; set; }
            public string zip { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }
        }

        private void GooglePlacesInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromHtml) return;

            var text = GooglePlacesInput.Text;
            string js = $"document.getElementById('pickup').value = `{EscapeJs(text)}`;";
            MapaWebView.ExecuteScriptAsync(js);
            //AutocompleteOverlay.ExecuteScriptAsync(js);
        }

        // Escape text for safe use in JavaScript
        private string EscapeJs(string input)
        {
            return input.Replace("\\", "\\\\").Replace("`", "\\`").Replace("\n", "").Replace("\r", "");
        }

        private async void OnSaveCustomerClick(object sender, RoutedEventArgs e)
        {
            // Customer save           
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            //culture = CultureInfo.InvariantCulture;

            var customer = new CustomerCreateDto
            {
                FullName = FullNameTextBox.Text,
                ClientCode = ClientCodeTextBox.Text,
                Phone = PhoneTextBox.Text,
                MobilePhone = MobilePhoneTextBox.Text,

                SpaceTypeId = Convert.ToInt16(SpaceTypeComboBox.SelectedValue), 
                FundingSourceId = Convert.ToInt16(FundingSourceComboBox.SelectedValue),

                Address = GooglePlacesInput.Text,
                City = City.Text,
                State = State.Text,
                Zip = Zip.Text,
                //DOB = DateTime.ParseExact(DOBDatePicker.Text, "MM/dd/yyyy", culture),
                Gender = MaleRadioButton.IsChecked == true? MaleRadioButton.Content.ToString(): FemaleRadioButton.Content.ToString(),

                Created = DateTime.Now,
                CreatedBy = SessionManager.Username

            };

            DateTime finalDob;

            if (DOBDatePicker.SelectedDate.HasValue)
            {
                finalDob = DOBDatePicker.SelectedDate.Value.Date;
            }
            else if (!string.IsNullOrWhiteSpace(DOBDatePicker.Text) && DateTime.TryParse(DOBDatePicker.Text, out var parsedDob))
            {
                
                finalDob = parsedDob.Date;
            }
            else
            {
                MessageBox.Show("Please select a valid date of birth.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
          
            customer.DOB = DateTime.SpecifyKind(finalDob, DateTimeKind.Unspecified);

            /*if (DOBDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Please select a valid date of birth.");
                return;
            }
            customer.DOB = DOBDatePicker.SelectedDate.Value;

            // ✅ Intentamos convertir la fecha con TryParse
            if (DateTime.TryParse(DOBDatePicker.Text, out var dob))
            {
                customer.DOB = dob;
            }
            else
            {
                MessageBox.Show("Invalid date of birth. Use a valid format.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }*/

            /*bool customerExists = false;
            var (updateSuccess, updateMessage) = (false,"");
            var (success, message, response) = (false, "", new object());*/

            bool success = false;

            CustomerService _customerService = new CustomerService();
            if (DataContext is HomeViewModel vm)
            {
                Customer selectedCustomer = vm.SelectedCustomer;
                if (selectedCustomer?.Id == null || selectedCustomer.Id == 0)
                {
                    try
                    {
                        var createdCustomer = await _customerService.CreateCustomerAsync(customer);
                        success = true;
                        vm.IdCustomer = createdCustomer.Id;

                        vm.SelectedCustomer = createdCustomer;                      
                        vm.Customers?.Add(createdCustomer);
                        vm.FilteredCustomers?.Add(createdCustomer);
                    }
                    catch (ApiException ex)
                    {
                        MessageBox.Show(
                            $"Error {ex.StatusCode}:\n{ex.ErrorDetails}",
                            "Server error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Unexpected error: {ex.Message}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                 
                }
                else
                {
                    try
                    {
                        //customer.Id = selectedCustomer.Id; MessageBox.Show(customer.ToString());
                        var updatedCustomer = await _customerService.UpdateCustomerAsync(selectedCustomer.Id, customer);
                        success = true;
                        vm.IdCustomer = selectedCustomer.Id;
                    }
                    catch (ApiException ex)
                    {
                        MessageBox.Show(
                            $"Error {ex.StatusCode}:\n{ex.ErrorDetails}",
                            "Server error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Unexpected error: {ex.Message}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                  
                }                   
            }

                       

            if (success)
            {
                if (DataContext is HomeViewModel vm2)
                {
                    vm2.IsOneWay = true;
                    vm2.IsRoundTrip = false;
                    vm2.IsAppointment = true;
                    vm2.IsReturn = false;
                    vm2.IsWillCall = false;
                }

                //MessageBox.Show(message); // luego crear servicios de mensajes, avisos y alertas

                CustomerColumn.Width = new GridLength(3.2, GridUnitType.Star);
                BillingColumn.Width = new GridLength(2.4, GridUnitType.Star); // 20%
                MapColumn.Width = new GridLength(4.4, GridUnitType.Star);
                MapaWebView.SetValue(Grid.ColumnProperty, 4); // Map moves to column 5 (index 4)
                BillingPanel.Visibility = Visibility.Visible;
                ForDateCalendar.Visibility = Visibility.Visible;


                // Hide travel filter
                TripFilterPanel.Visibility = Visibility.Collapsed;

                // Show TabControl
                TripTabs.Visibility = Visibility.Visible;
                //TripTabs.SetValue(Grid.RowProperty, 1);
               // PickupAddressTextBox.Text = GooglePlacesInput.Text;

                // Adjust row size
                TopRow.Height = new GridLength(6.73, GridUnitType.Star);
                BottomRow.Height = new GridLength(3.27, GridUnitType.Star);

                // Show Dropoff input in WebView map
                await MapaWebView.ExecuteScriptAsync("showDropoff();");
            }
            else
            {
                // MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        public void ResetLayoutToInitialState()
        {
            // 1. Restaurar anchos de columnas
            CustomerColumn.Width = new GridLength(1, GridUnitType.Star);
            BillingColumn.Width = new GridLength(0, GridUnitType.Pixel); // Ocultar Billing
            MapColumn.Width = new GridLength(2, GridUnitType.Star);

            // 2. Mover el mapa de vuelta a su posición original (Columna 4)
            MapaWebView.SetValue(Grid.ColumnProperty, 4);

            // 3. Visibilidad de Paneles
            BillingPanel.Visibility = Visibility.Hidden;
            ForDateCalendar.Visibility = Visibility.Collapsed;
            TripTabs.Visibility = Visibility.Collapsed;
            TripFilterPanel.Visibility = Visibility.Visible; // Mostrar filtros de nuevo

            // 4. Restaurar tamaño de filas (Top 45% / Bottom 55%)
            TopRow.Height = new GridLength(4.5, GridUnitType.Star);
            BottomRow.Height = new GridLength(5.5, GridUnitType.Star);

            // 5. Resetear el Mapa (JavaScript)
            // Esto limpiará los marcadores y ocultará el input de Dropoff
            MapaWebView.ExecuteScriptAsync("prepareNewCustomer();");

            // 6. Limpiar campos de búsqueda (Opcional)
            CustomersAutoSuggestBox.Text = string.Empty;
        }

        private async void OnNewCustomerClick(object sender, RoutedEventArgs e) {
            await MapaWebView.ExecuteScriptAsync("prepareNewCustomer();");

        }

        private void CustomersAutoSuggestBox_SuggestionChosen(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Patch to autocomplete bug.
            if (DataContext is HomeViewModel vm)
            {              
                Customer customer = vm.Customers.FirstOrDefault(c => c.Id == int.Parse(e.NewValue.ToString()));

                // MessageBox.Show(a?.FullName + " a.FullName");
                vm.SelectedCustomer = customer;
                vm.SearchText = customer?.FullName;

                ShowPickupInMap();
            }
        }

        private void CustomersAutoSuggestBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void AppointmentRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PickupTimePicker.Visibility = Visibility.Visible;
            ApptTimePicker.Visibility = Visibility.Visible;
            ReturnTimePicker.Visibility = Visibility.Collapsed;
            WillCallCheckBox.Visibility = Visibility.Collapsed;

            string tripType = TripType.Appointment;
            //TripTypeTextBlock.Text = tripType;  
            BillingSectionTabItem.Header = tripType + " " + ForDateCalendar.SelectedDate.Value.Date.ToShortDateString() + " " + "Hora";
            if (DataContext is HomeViewModel vm)
            {
                vm.TripType = tripType;
            }
        }

        private void ReturnRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PickupTimePicker.Visibility = Visibility.Collapsed;
            ApptTimePicker.Visibility = Visibility.Collapsed;
            ReturnTimePicker.Visibility = Visibility.Visible;
            WillCallCheckBox.Visibility = Visibility.Visible;

            string tripType = TripType.Return;
            TripTypeTextBlock.Text = tripType;
            BillingSectionTabItem.Header = TripType.Return + " " + ForDateCalendar.SelectedDate.Value.Date.ToShortDateString() + " " + "Hora";
        }

        private void GeolocateFloatingButton_MouseEnter(object sender, MouseEventArgs e)
        {
            Color primaryColor;
            SolidColorBrush backgroundBrush;
            if (GeolocateFloatingButton.Background is SolidColorBrush brush)
            {
                primaryColor = brush.Color;
            }

            GeolocateFloatingButton.Background = Brushes.Transparent;

            // Crear animaciones
            var widthAnimation = new DoubleAnimation(35, TimeSpan.FromSeconds(0.3));
            var heightAnimation = new DoubleAnimation(35, TimeSpan.FromSeconds(0.3));
            var colorAnimation = new ColorAnimation(primaryColor, TimeSpan.FromSeconds(0.3));

            // Configurar interpolación suave
            widthAnimation.EasingFunction = new QuadraticEase();
            heightAnimation.EasingFunction = new QuadraticEase();

            // Aplicar animaciones
            GeolocateFloatingButton.BeginAnimation(WidthProperty, widthAnimation);
            GeolocateFloatingButton.BeginAnimation(HeightProperty, heightAnimation);

            // Animación del color de fondo
            backgroundBrush = new SolidColorBrush(Colors.Transparent);
            GeolocateFloatingButton.Background = backgroundBrush;
            backgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
        }

        private void GeolocateFloatingButton_MouseLeave(object sender, MouseEventArgs e)
        {
            SolidColorBrush backgroundBrush = new SolidColorBrush(Colors.Transparent);

            if (GeolocateFloatingButton.Background is SolidColorBrush brush)
            {
                backgroundBrush = new SolidColorBrush(brush.Color);
            }
            // Reverse animations
            var widthAnimation = new DoubleAnimation(30, TimeSpan.FromSeconds(0.3));
            var heightAnimation = new DoubleAnimation(30, TimeSpan.FromSeconds(0.3));
            var colorAnimation = new ColorAnimation(Colors.Transparent, TimeSpan.FromSeconds(0.3));

            // Set up soft interpolation
            widthAnimation.EasingFunction = new QuadraticEase();
            heightAnimation.EasingFunction = new QuadraticEase();

            // Apply animations
            GeolocateFloatingButton.BeginAnimation(WidthProperty, widthAnimation);
            GeolocateFloatingButton.BeginAnimation(HeightProperty, heightAnimation);

            // Background color animation
            backgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
        }

        private void GeolocateFloatingButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPickupInMap();           
        }

        private async void ShowPickupInMap()
        {
            var street = GooglePlacesInput.Text;
            var city = City.Text;
            var state = State.Text;
            var zip = Zip.Text;
            
            var fullAddress = $"{street}, {city}, {state}, {zip}";

            GoogleMapsService googleMapsService = new GoogleMapsService();
            var coordinates = await googleMapsService.GetCoordinatesFromAddress(fullAddress);           
            //var coordinates = await GetCoordinatesFromAddress(fullAddress);

            if (coordinates != null)
            {
                //position = { lat, lng }
                // Format using CultureInfo.InvariantCulture to avoid issues with commas/points
                var positionJson = $"{{ lat: {coordinates.Latitude.ToString(CultureInfo.InvariantCulture)}, lng: {coordinates.Longitude.ToString(CultureInfo.InvariantCulture)} }}";
                string script = $@"
                    if (typeof setPickupMarker === 'function') {{
                        setPickupMarker({positionJson});
                    }} else {{
                        console.error('setPickupMarker function is not defined');
                    }}
                ";
                //string script = $"setPickupMarker({positionJson});"; 
                // Call a JS function in the WebView to locate on the map
                //string script = $"setPickupMarker({coordinates.Latitude}, {coordinates.Longitude});"; 

                await MapaWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            else
            {
                MessageBox.Show("Could not get location", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RoundTripRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            AppointmentRadioButton.Visibility = Visibility.Collapsed;
            ReturnRadioButton.Visibility = Visibility.Collapsed;

            PickupTimePicker.Visibility = Visibility.Visible;
            ApptTimePicker.Visibility = Visibility.Visible;
            ReturnTimePicker.Visibility = Visibility.Visible;
            WillCallCheckBox.Visibility = Visibility.Visible;
        }

        private void OneWayRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            AppointmentRadioButton.Visibility = Visibility.Visible;
            ReturnRadioButton.Visibility = Visibility.Visible;
        }

        private void DropoffAddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromHtml) return;

            var text = DropoffAddressTextBox.Text;
            string js = $"document.getElementById('dropoff').value = `{EscapeJs(text)}`;";
            MapaWebView.ExecuteScriptAsync(js);
            //AutocompleteOverlay.ExecuteScriptAsync(js);
        }

        // State variable to control the icon
        private bool isAutorenewOn = false;
        private double currentRotation = 0;
        private void InverterFloatingButton_Click(object sender, RoutedEventArgs e)
        {       
            // Toggle status
            isAutorenewOn = !isAutorenewOn;

            // Change icon based on status
            InverterIcon.Kind = isAutorenewOn ?
                MaterialDesignThemes.Wpf.PackIconKind.Autorenew :
                MaterialDesignThemes.Wpf.PackIconKind.AutorenewOff;

            //invertir los valores y luego llamar a ShowPickupInMap() y ShowDropoffInMap()
        }

        private void SaveTab1FloatingButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            GridRow0.Visibility = Visibility.Collapsed;
            GridRow1.Visibility = Visibility.Collapsed;
            GridRow2.Visibility = Visibility.Collapsed;
            ImportTripsGridRow.Visibility = Visibility.Visible;
        }

        private void BackToHome_Click(object sender, RoutedEventArgs e)
        {
            // Ocultamos la vista de importación
            ImportTripsGridRow.Visibility = Visibility.Collapsed;

            // Volvemos a mostrar los componentes principales
            GridRow0.Visibility = Visibility.Visible;
            GridRow1.Visibility = Visibility.Visible;
            GridRow2.Visibility = Visibility.Visible;

            // Opcional: Limpiar el DataGrid de vista previa al salir
            PreviewGrid.ItemsSource = null;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }

        private async void SelectCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                Title = "Select CSV file"
            };

            if (dialog.ShowDialog() == true)
            {
                // Disable button and show progress
                SelectCsvButton.IsEnabled = false;
                ProgressPanel.Visibility = Visibility.Visible;
                ImportProgressBar.Value = 0;
                ImportProgressBar.Maximum = 1; // Will update after reading the records
                ProgressText.Text = "Reading CSV file...";
                PreviewGrid.ItemsSource = null; // Clear previous preview

                try
                {
                    bool isSaferide = true;
                    // Read the CSV (this is still sequential and may take time)
                    // Consider making ReadCsv also asynchronous and report its progress if it is very large.
                    List<CsvTripRawModel> records;
                    CsvReaderService csvReaderService = new CsvReaderService(dialog.FileName);
                    isSaferide = csvReaderService.IsSaferide();
                    CsvType csvType = csvReaderService.GetCsvType();
                    try
                    {
                        
                        string jsonFileName = string.Empty;
                        /*jsonFileName = GetJsonFileName(csvType);
                        string GetJsonFileName(CsvType csvType) => csvType switch
                        {
                            CsvType.Saferide => "SAFERIDE.json",
                            CsvType.Saferide2 => "SAFERIDE2.json",
                            CsvType.Ride2md => "Ride2md.json",
                            _ => throw new ArgumentOutOfRangeException(nameof(csvType), csvType, null)
                        };*/
                        switch (csvType)
                        {
                            case CsvType.Saferide:
                                jsonFileName = "SAFERIDE.json";
                                break;
                            case CsvType.Saferide2:
                                jsonFileName = "SAFERIDE2.json";
                                break;
                            case CsvType.Ride2md:
                                jsonFileName = "Ride2md.json";
                                bool correctFormat = csvReaderService.IsRide2mdCorrectFormat();
                                if (!correctFormat)
                                {
                                    MessageBox.Show("The selected Ride2md CSV file does not have the correct format. Please check the file and try again.", "Format Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    return; // Exit if format is incorrect
                                }
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(csvType), csvType, "CSV type not supported");
                                
                        }
                        //var jsonFileName = isSaferide ? "SAFERIDE.json" : "Ride2md.json"; 

                        // If ReadCsv may take a long time, consider async Task<List<CsvTripRawModel>>
                        // and run it with Task.Run().
                        //records = await Task.Run(() => ReadCsv(dialog.FileName)); // Run in a background thread to not block UI.
                        records = await Task.Run(() => csvReaderService.ReadCsvWithDuplicateColumns(jsonFileName)); // Run in a background thread to not block UI.
                        //PreviewGrid.ItemsSource = records;
                    }
                    catch (Exception readEx)
                    {
                        MessageBox.Show($"Error reading CSV file: {readEx.Message}", "Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return; // Exit if reading fails
                    }
                    finally
                    {
                        // Always reactivate the button and hide progress if something fails in reading or at the end
                        SelectCsvButton.IsEnabled = true;
                        // Don't hide ProgressPanel yet, we will do it after mapping
                    }


                    if (records == null || !records.Any())
                    {
                        MessageBox.Show("The CSV file is empty or no records could be read.", "Empty File", MessageBoxButton.OK, MessageBoxImage.Information);
                        ProgressPanel.Visibility = Visibility.Collapsed;
                        return;
                    }

                    ImportProgressBar.Maximum = records.Count; // Update the maximum value of the progress bar.
                    ProgressText.Text = $"Processing 0 of {records.Count} trips...";

                    // Map logs in parallel with SemaphoreSlim and progress 
                    if (DataContext is HomeViewModel vm)
                    {
                        GoogleMapsService googleMapsService = new GoogleMapsService();
                        ISpaceTypeService spaceTypeService = new SpaceTypeService();
                        ICapacityTypeService capacityTypeService = new CapacityTypeService();
                        ICustomerService customerService = new CustomerService();
                        IFundingSourceService fundingSourceService = new FundingSourceService();
                        TripService tripService = new TripService();
                       
                        var mapper = new CsvTripMapper(
                            vm.Trips, vm.SpaceTypes, vm.CapacityTypes, vm.Customers, vm.FundingSources,
                            googleMapsService, spaceTypeService, capacityTypeService,
                            customerService, fundingSourceService, tripService
                        );

                        FundingSource importSelectedFundingSource = vm.SelectedFundingSourceImport;
                        // fundingSourceName = "SAFERIDE"; 
                        bool selectedFileIsSaferide = isSaferide;
                        //bool selectedFundingSourceIsSaferide = string.Equals(importSelectedFundingSource.Name, "SAFERIDE", StringComparison.OrdinalIgnoreCase);

                        // para los casos de "SAFERIDE MILANES" Y "SAFERIDE YAMIGROUP"
                        bool selectedFundingSourceIsSaferide = importSelectedFundingSource.Name?.StartsWith("SAFERIDE", StringComparison.OrdinalIgnoreCase) ?? false;

                        // If the uploaded file does not correspond to the selected FundingSource, display a message and do not allow the file to be imported
                        if ((selectedFileIsSaferide && !selectedFundingSourceIsSaferide) || (!selectedFileIsSaferide && selectedFundingSourceIsSaferide))
                        {
                            ShowInconsistencyMessage();
                        }
                        else 
                        {
                            List<CsvTripRawModel> errorTrips = new List<CsvTripRawModel>();
                            // If the FundingSource is SAFERIDE, the number of concurrent threads must be limited because it does not have the Coordinates and many requests cannot be made to the Google Maps API
                            // SAFERIDE = 5
                            // Ride2md = 10
                            int maxConcurrentTasks = selectedFileIsSaferide ? 5 : 10; // Here configure the maximum number of simultaneous processes. Keep in mind that The free Google Maps Api option only allows (50 requests per second, 3000 per minute) and each trip makes 2 calls: pickupAddress and dropoffAddress.
                            using var semaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);

                            var mappedTrips = new List<TripReadDto>(records.Count);
                            var mappingTasks = new List<Task>();
                            int processedCount = 0;

                            // Set up the progress reporter
                            var progressReporter = new Progress<int>(processed =>
                            {
                                ImportProgressBar.Value = processed;
                                ProgressText.Text = $"Processing {processed} of {records.Count} trips...";
                            });

                            int count = 0;
                            foreach (var record in records)
                            {
                                await semaphore.WaitAsync(); // Wait for a slot

                                var task = Task.Run(async () => // Use Task.Run to not block the SelectCsv_Click loop
                                {
                                    try 
                                    {
                                        
                                        var trip = await mapper.MapToTripAsync(record, importSelectedFundingSource, selectedFileIsSaferide, csvType);
                                        lock (mappedTrips) // Synchronize access to the shared list
                                        {
                                            mappedTrips.Add(trip);
                                        }
                                        Interlocked.Increment(ref processedCount); // Atomic increase
                                        ((IProgress<int>)progressReporter).Report(processedCount);
                                        count = processedCount;
                                    }
                                    catch (Exception taskEx)
                                    {
                                        // Handle errors per task individually if necessary
                                        // For example, log the error and continue
                                        Debug.WriteLine($"Error mapping record: {taskEx.Message}");
                                        // Optionally, you can add this error to a list of errors to display at the end
                                        errorTrips.Add(record);
                                    }
                                    finally
                                    {
                                        semaphore.Release(); // Release the slot
                                    }
                                });
                                mappingTasks.Add(task);
                            }

                            // Wait for all mapping tasks to complete
                            await Task.WhenAll(mappingTasks);

                            string errorTripIdList = ""; // string.Join(", ", mappedTrips.Select(t => t.Id));
                            // Resolving the concurrency error
                            // Try to insert the trips that gave a concurrency error when inserting the customer.
                            foreach (var et in errorTrips) 
                            {
                                try
                                {
                                    var trip = await mapper.MapToTripAsync(et, importSelectedFundingSource, selectedFileIsSaferide, csvType);
                                    mappedTrips.Add(trip);
                                    count++;
                                    ((IProgress<int>)progressReporter).Report(count);
                                }
                                catch (Exception ex)
                                {
                                    errorTripIdList = errorTripIdList + et.RideId + ", ";
                                    Debug.WriteLine($"Error mapping record: {ex.Message}");
                                }
                                
                            }

                            AssignTripTypes(mappedTrips);
                           
                            var updatePayload = mappedTrips.Select(t => new TripTypeUpdateDto
                            {
                                Id = t.Id,
                                Type = t.Type
                            }).ToList();

                            await tripService.UpdateTripTypeAsync(updatePayload);

                            //PreviewGrid.ItemsSource = new ObservableCollection<Trip>(mappedTrips); // Use ObservableCollection if the UI needs to be updated dynamically
                            PreviewGrid.ItemsSource = mappedTrips;

                            // Variant of saving all trips in a single request to the API
                            //await tripService.CreateTripsAsync(mappedTrips); 
                            MessageBox.Show($"{mappedTrips.Count} Trips processed and ready to preview.", "Process Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                            if(errorTrips.Count  > 0) 
                                MessageBox.Show($"{errorTripIdList} Lista de ids de trips sin importar.", "Process Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        
                    }
                    else
                    {
                        MessageBox.Show("ViewModel not found. Can't continue.", "Context Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex) // General catch for unexpected errors
                {
                    MessageBox.Show($"General error during import:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    // Ensure the UI is restored
                    SelectCsvButton.IsEnabled = true;
                    ProgressPanel.Visibility = Visibility.Collapsed; // Hide progress panel
                    ImportProgressBar.Value = 0; // Reset bar
                }
            }
        }

        public void AssignTripTypes(List<TripReadDto> trips)
        {
            // Agrupamos por cliente para analizar sus viajes del día
            var tripsByCustomer = trips.GroupBy(t => t.CustomerId);

            foreach (var group in tripsByCustomer)
            {
                var customerTrips = group.OrderBy(t => t.FromTime).ToList();

                if (customerTrips.Count == 1)
                {
                    var trip = customerTrips.First();
                    // Regla: Si es único y WillCall es true, es Return. Si no, Appointment.
                    trip.Type = trip.WillCall ? "Return" : "Appointment";
                }
                else if (customerTrips.Count >= 2)
                {
                    // El primero (más temprano) es Appointment
                    customerTrips.First().Type = "Appointment";

                    // El último (más tarde) es Return
                    customerTrips.Last().Type = "Return";

                    // Si hubiera viajes intermedios (más de 2), podrías decidir qué hacer.
                    // Por ahora, marcamos los del medio como Return
                    for (int i = 1; i < customerTrips.Count - 1; i++)
                    {
                        customerTrips[i].Type = "Return";
                    }
                }
            }
        }

        private void ShowInconsistencyMessage() 
        {
            MessageBox.Show("The data loaded in the file does not correspond to the selected Funding Source");
        }

        // If ReadCsv may take a long time, consider async Task<List<CsvTripRawModel>>
        // and run it with Task.Run().
        private List<CsvTripRawModel> ReadCsv(string filePath)
        {

            // Define CsvHelper settings
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = (args) =>
                {
                    Console.WriteLine($"Missing field found. Headers: '{string.Join(", ", args.HeaderNames ?? new string[0])}', Index: {args.Index}, Row: {args.Context.Parser.Row}");
                },
            };

            using var reader = new StreamReader(filePath);
            using var csv = new CsvHelper.CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();
            var actualCsvHeaders = csv.Context.Reader.HeaderRecord;
            if (actualCsvHeaders == null)
            {
                throw new InvalidOperationException("The CSV file contains no headers or is empty.");
            }

            // Remove duplicate columns
            var uniqueHeaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var headerOriginalIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < actualCsvHeaders.Length; i++)
            {
                var header = actualCsvHeaders[i];
                if (!uniqueHeaderNames.Contains(header))  
                {
                    uniqueHeaderNames.Add(header);
                    headerOriginalIndices[header] = i;
                }
            }

            string mappingFileName = "SAFERIDE.json"; 
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string mappingPath = System.IO.Path.Combine(baseDirectory, "Assets", "Mappings", mappingFileName);

            if (!File.Exists(mappingPath))
            {
                throw new FileNotFoundException($"The mapping file was not found: {mappingPath}");
            }

            var mappingJson = File.ReadAllText(mappingPath); 
            var tempMapping = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(mappingJson);

            if (tempMapping == null)
            {
                throw new Exception($"Error deserializing JSON mapping file: {mappingFileName}.");
            }
            var csvToPropertyMappings = new Dictionary<string, string>(tempMapping, StringComparer.OrdinalIgnoreCase);

            var map = new CsvTripRawModelMap(headerOriginalIndices, csvToPropertyMappings); 
            csv.Context.RegisterClassMap(map);

            return csv.GetRecords<CsvTripRawModel>().ToList();
        }
                    

        /*private CsvTripRawModel MapModel(dynamic record, Dictionary<string, string> mapping)
        {
            CsvTripRawModel raw = new CsvTripRawModel();
            
            raw.RideId = GetValue(record, mapping, "RideId");
            raw.Type = GetValue(record, mapping, "Type");
            raw.Status = GetValue(record, mapping, "Status");

            raw.FromSt = GetValue(record, mapping, "FromSt");
            raw.FromCity = GetValue(record, mapping, "FromCity");
            raw.FromState = GetValue(record, mapping, "FromState");
            raw.FromZIP = GetValue(record, mapping, "Type");

            raw.ToST = GetValue(record, mapping, "ToSt");
            raw.ToCity = GetValue(record, mapping, "ToCity");
            raw.ToState = GetValue(record, mapping, "ToState");
            raw.ToZip = GetValue(record, mapping, "ToZip");

            raw.Date = GetValue(record, mapping, "Date");
            raw.PickupTime = GetValue(record, mapping, "PickupTime");
            raw.Appointment = GetValue(record, mapping, "Appointment");
            raw.CancelledDate = GetValue(record, mapping, "CancelledDate");

            raw.PatientFirstName = GetValue(record, mapping, "PatientFirstName");
            raw.PatientLastName = GetValue(record, mapping, "PatientLastName");
            raw.PatientPhoneNumber = GetValue(record, mapping, "PatientPhoneNumber");
            raw.AlternativePhoneNumber = GetValue(record, mapping, "AlternativePhoneNumber");
            raw.Gender = GetValue(record, mapping, "Gender");

            raw.Treatment = GetValue(record, mapping, "Treatment");
            raw.Distance = GetValue(record, mapping, "Distance");
            raw.AdditionalNotes = GetValue(record, mapping, "AdditionalNotes");
            raw.OtherDetails = GetValue(record, mapping, "OtherDetails");

            raw.CancelledBy = GetValue(record, mapping, "CancelledBy");
            raw.CancelledReasonType = GetValue(record, mapping, "CancelledReasonType");
            raw.CancelledReasonMessage = GetValue(record, mapping, "CancelledReasonMessage");

            return raw;
        }
        private string GetValue(dynamic record, Dictionary<string, string> mapping, string rawModelPropertyName)
        {
            if (mapping.TryGetValue(rawModelPropertyName, out var csvColumnName))
            {
                var dict = (IDictionary<string, object>)record;
                return dict.ContainsKey(csvColumnName) ? dict[csvColumnName]?.ToString() : null;
            }
            return null;
        }*/
    }
}
