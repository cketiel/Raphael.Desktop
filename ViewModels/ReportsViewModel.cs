using ClosedXML.Excel;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Models.Pdf;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Raphael.Desktop.ViewModels
{
    public class ReportsViewModel : BaseViewModel
    {
        private readonly FundingSourceService _fundingSourceService;
        private readonly ScheduleService _scheduleService;
        private readonly GpsService _gpsService;
        private readonly RunService _vehicleRouteService;

        private bool _isAllRoutesSelected;
        public bool IsAllRoutesSelected
        {
            get => _isAllRoutesSelected;
            set
            {
                if (_isAllRoutesSelected != value)
                {
                    _isAllRoutesSelected = value;
                    OnPropertyChanged();
                    ToggleAllRoutes(value);
                }
            }
        }

        private bool _isUpdatingRoutesSelection = false;

        public ObservableCollection<FundingSource> AllFundingSources { get; set; } = new ObservableCollection<FundingSource>();
        public ObservableCollection<VehicleRoute> AllVehicleRoutes { get; set; } = new ObservableCollection<VehicleRoute>();
        public FundingSource SelectedFundingSource { get; set; }
        public VehicleRoute SelectedVehicleRoute { get; set; }
        public DateTime SelectedDate { get; set; } = DateTime.Today;
        public bool IsPreviewReady { get; set; }
        public string PreviewFilePath { get; set; }
        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set { _isGenerating = value; OnPropertyChanged(); }
        }
        public string GenerateButtonText { get; set; } = "Generate Production Report";
        private bool _isGeneratingGps;
        public string GenerateGpsButtonText { get; set; } = "Generate GPS Report";
        private bool _isGeneratingTrip2;
        public string GenerateTrip2ButtonText { get; set; } = "Generate Trip2 PDF";

        // Trip2 Properties
        private bool _isTrip2AllSelected;
        private bool _isGeneratingTrip2Preview;
        private bool _isTrip2PreviewReady;
        private string _trip2PreviewFilePath;

        public DateTime Trip2StartDate { get; set; } = DateTime.Today;
        public DateTime Trip2EndDate { get; set; } = DateTime.Today;

        public bool IsGeneratingTrip2Preview { get => _isGeneratingTrip2Preview; set { _isGeneratingTrip2Preview = value; OnPropertyChanged(); } }
        public bool IsTrip2PreviewReady { get => _isTrip2PreviewReady; set { _isTrip2PreviewReady = value; OnPropertyChanged(); } }
        public string Trip2PreviewFilePath { get => _trip2PreviewFilePath; set { _trip2PreviewFilePath = value; OnPropertyChanged(); } }

        public bool IsTrip2AllSelected
        {
            get => _isTrip2AllSelected;
            set
            {
                if (_isTrip2AllSelected != value)
                {
                    _isTrip2AllSelected = value;
                    OnPropertyChanged();
                    ToggleAllFundingSources(value); // You can reuse the existing toggle method
                }
            }
        }

        // Aviata Properties
        private bool _isAllSelected;
        private bool _isUpdatingSelection = false; // Flag to prevent infinite loops
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today;
        private bool _isGeneratingAviata;
        public string GenerateAviataButtonText { get; set; } = "Generate Aviata Report";
        public bool IsGeneratingAviata { get; set; }
        public string AviataPreviewFilePath { get; set; }
        public bool IsPreviewReadyAviata { get; set; }
        public bool IsNotDriver { get; set; }

        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (_isAllSelected != value)
                {
                    _isAllSelected = value;
                    OnPropertyChanged();
                    ToggleAllFundingSources(value);
                }
            }
        }

        // Logic for "Select All" CheckBox
        /*public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (_isAllSelected != value)
                {
                    _isAllSelected = value;
                    OnPropertyChanged();
                    ToggleAllSelection(value); // Select or Deselect all items
                }
            }
        }*/

        // Commands
        public ICommand PreviewProductionReportCommand { get; }
        public ICommand GenerateProductionReportCommand { get; }
        public ICommand GenerateGpsReportCommand { get; }
        public ICommand GenerateTrip2PdfCommand { get; }
        public ICommand GenerateAviataReportCommand { get; }
        public ICommand PreviewAviataReportCommand { get; }
        public ICommand SaveAviataReportCommand { get; }
        public ICommand PreviewTrip2ReportCommand { get; }
        public ICommand SaveTrip2ReportCommand { get; }

        public ReportsViewModel()
        {
            string currentRole = SessionManager.Role;
            bool IsDriver = currentRole == "2";
            IsNotDriver = !IsDriver;

            _fundingSourceService = new FundingSourceService();
            _scheduleService = new ScheduleService();
            _gpsService = new GpsService();
            _vehicleRouteService = new RunService();

            // We pass the async method to the command and manage the execution state.
            GenerateProductionReportCommand = new RelayCommandObject(async (o) => await GenerateProductionReport(o), (o) => !_isGenerating);
            GenerateGpsReportCommand = new RelayCommandObject(async (o) => await GenerateGpsReport(o), (o) => !_isGeneratingGps && SelectedVehicleRoute != null);
            GenerateTrip2PdfCommand = new RelayCommandObject(async (o) => await GenerateTrip2Report(o), (o) => !_isGeneratingTrip2);
            //GenerateAviataReportCommand = new RelayCommandObject(async (o) => await GenerateAviataReport(o));
            //GenerateAviataReportCommand = new RelayCommandObject(async (o) => await GenerateAviataReport(o), (o) => !_isGeneratingAviata);

            PreviewTrip2ReportCommand = new RelayCommandObject(async (o) => await GenerateTrip2Preview());
            SaveTrip2ReportCommand = new RelayCommandObject((o) => SaveTrip2PreviewToFile());
            PreviewAviataReportCommand = new RelayCommandObject(async (o) => await GenerateAviataPreview());
            SaveAviataReportCommand = new RelayCommandObject((o) => SavePreviewToFile());

            PreviewProductionReportCommand = new RelayCommandObject(async (o) => await GenerateProductionPreview());

            LoadData();
           
        }

        private async Task GenerateProductionPreview()
        {
            IsGenerating = true; // Vinculado al ProgressBar
            IsPreviewReady = false;
            OnPropertyChanged(nameof(IsGenerating));
            OnPropertyChanged(nameof(IsPreviewReady));

            try
            {
                // 1. Obtener IDs seleccionados
                var selectedFsIds = AllFundingSources.Where(x => x.IsSelected && x.Id != -1).Select(x => x.Id).ToList();
                var selectedRouteIds = AllVehicleRoutes.Where(x => x.IsSelected).Select(x => x.Id).ToList();

                // 2. Llamar al servicio 
                var reportData = await _scheduleService.GetProductionReportDataRangeAsync(StartDate, EndDate, selectedFsIds, selectedRouteIds);

                if (reportData == null || !reportData.Any())
                {
                    MessageBox.Show("No data found for the selected filters.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 3. Generar PDF temporal para visualización (Usando QuestPDF)
                QuestPDF.Settings.License = LicenseType.Community;
                string fileName = $"ProdPreview_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

                var document = new ProductionDocument(reportData, StartDate, EndDate);              
                document.GeneratePdf(tempPath);

                // 4. Mostrar en UI
                PreviewFilePath = new Uri(tempPath).AbsoluteUri;
                IsPreviewReady = true;
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
            finally
            {
                IsGenerating = false;
                OnPropertyChanged(nameof(IsGenerating));
                OnPropertyChanged(nameof(IsPreviewReady));
                OnPropertyChanged(nameof(PreviewFilePath));
            }
        }

        private async void LoadData()
        {            
            string currentRole = SessionManager.Role;
            bool IsDriver = currentRole == "2";
            try
            {
                // Limpiamos antes de cargar para evitar duplicados
                AllFundingSources.Clear();
                AllVehicleRoutes.Clear();

                var sources = await _fundingSourceService.GetFundingSourcesAsync(false);
                foreach (var s in sources)
                {
                    s.IsSelected = false;
                    s.PropertyChanged += OnFundingSourceSelectionChanged;
                    AllFundingSources.Add(s);
                }

                var routes = await _vehicleRouteService.GetAllAsync();
                
                if (IsDriver)
                {
                    string currentUser = SessionManager.UserId;
                    int userId = int.Parse(currentUser);
                    var r = routes.Find(r => r.DriverId == userId);
                    r.IsSelected = true;
                    r.PropertyChanged += OnRouteSelectionChanged;
                    AllVehicleRoutes.Add(r);
                }
                else
                {
                    foreach (var r in routes)
                    {
                        r.IsSelected = false;
                        r.PropertyChanged += OnRouteSelectionChanged; // Suscribirse
                        AllVehicleRoutes.Add(r);
                    }
                }                 

                SelectedVehicleRoute = AllVehicleRoutes.FirstOrDefault();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
        }
        private void OnRouteSelectionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VehicleRoute.IsSelected) && !_isUpdatingRoutesSelection)
            {
                _isUpdatingRoutesSelection = true;
                IsAllRoutesSelected = AllVehicleRoutes.All(x => x.IsSelected);
                _isUpdatingRoutesSelection = false;
            }
        }

        private void ToggleAllRoutes(bool selectAll)
        {
            if (_isUpdatingRoutesSelection) return;
            _isUpdatingRoutesSelection = true;
            foreach (var route in AllVehicleRoutes) route.IsSelected = selectAll;
            _isUpdatingRoutesSelection = false;
        }

        private async void LoadDataOld()
        {
            try
            {
                // Load Funding Sources
                var sources = await _fundingSourceService.GetFundingSourcesAsync(false);
                foreach (var s in sources)
                {
                    s.IsSelected = false;
                    s.PropertyChanged += OnFundingSourceSelectionChanged; // Subscribe to changes
                    AllFundingSources.Add(s);
                }
                SelectedFundingSource = AllFundingSources.FirstOrDefault();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            try
            {
                /*var sources = await _fundingSourceService.GetFundingSourcesAsync(false);
                foreach (var s in sources) AllFundingSources.Add(s);
                SelectedFundingSource = AllFundingSources.FirstOrDefault();*/

                var routes = await _vehicleRouteService.GetAllAsync();
                foreach (var r in routes) AllVehicleRoutes.Add(r);
            }
            catch { /* Manejar error silencioso para no romper UI */ }
        }

        // Logic to Select/Deselect all items
        private void ToggleAllFundingSources(bool selectAll)
        {
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;

            foreach (var source in AllFundingSources)
                source.IsSelected = selectAll;

            _isUpdatingSelection = false;
        }

        // Sync "Select All" CheckBox when individual items are clicked
        private void OnFundingSourceSelectionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FundingSource.IsSelected) && !_isUpdatingSelection)
            {
                _isUpdatingSelection = true;
                IsAllSelected = AllFundingSources.All(x => x.IsSelected);
                _isUpdatingSelection = false;
            }
        }

        // Logic to select/deselect all items when "Select All" is toggled
        private void ToggleAllSelection(bool isSelected)
        {
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;

            foreach (var source in AllFundingSources)
            {
                source.IsSelected = isSelected;
            }

            _isUpdatingSelection = false;
        }

        // Updates "IsAllSelected" state based on individual item changes
        private void OnFundingSourcePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FundingSource.IsSelected) && !_isUpdatingSelection)
            {
                _isUpdatingSelection = true;
                // If all items are selected, check "Select All", otherwise uncheck it
                IsAllSelected = AllFundingSources.All(x => x.IsSelected);
                _isUpdatingSelection = false;
            }
        }

        private async Task GenerateTrip2Preview()
        {
            IsGeneratingTrip2Preview = true;
            IsTrip2PreviewReady = false;
            OnPropertyChanged(nameof(IsTrip2PreviewReady));

            try
            {
                // 1. Collect selected IDs
                var selectedIds = AllFundingSources.Where(x => x.IsSelected && x.Id != -1).Select(x => x.Id).ToList();

                // 2. Fetch data (Using the range method)
                var reportData = await _scheduleService.GetTrip2ReportDataAsync(Trip2StartDate, Trip2EndDate, selectedIds);

                if (reportData == null || !reportData.Any())
                {
                    MessageBox.Show("No data found for Trip2 with selected filters.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 3. Generate PDF
                QuestPDF.Settings.License = LicenseType.Community;
                var groupedByDriver = reportData.GroupBy(d => d.Driver ?? "Unknown Driver").ToList();

                // Dynamic filename to force WebView2 refresh
                string fileName = $"Trip2Preview_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

                var document = new Trip2Document(groupedByDriver, Trip2StartDate); // Or handle range in document
                document.GeneratePdf(tempPath);

                // 4. Update UI
                Trip2PreviewFilePath = new Uri(tempPath).AbsoluteUri;
                IsTrip2PreviewReady = true;
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
            finally
            {
                IsGeneratingTrip2Preview = false;
                OnPropertyChanged(nameof(IsGeneratingTrip2Preview));
                OnPropertyChanged(nameof(IsTrip2PreviewReady));
                OnPropertyChanged(nameof(Trip2PreviewFilePath));
            }
        }

        private void SaveTrip2PreviewToFile()
        {
            if (string.IsNullOrEmpty(Trip2PreviewFilePath)) return;
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "PDF Document|*.pdf",
                FileName = $"Trip2_{Trip2StartDate:MM-dd-yyyy}_{Trip2EndDate:MM-dd-yyyy}.pdf"
            };

            if (sfd.ShowDialog() == true)
            {
                string localPath = new Uri(Trip2PreviewFilePath).LocalPath;
                System.IO.File.Copy(localPath, sfd.FileName, true);
                MessageBox.Show("Trip2 Report saved!");
            }
        }

        private async Task GenerateAviataPreview()
        {
            IsGeneratingAviata = true; IsPreviewReadyAviata = false;
            OnPropertyChanged(nameof(IsGeneratingAviata));
            OnPropertyChanged(nameof(IsPreviewReadyAviata));

            try
            {
                // Get only selected IDs for the report
                var selectedIds = AllFundingSources.Where(x => x.IsSelected && x.Id != -1).Select(x => x.Id).ToList();

                var reportData = await _scheduleService.GetAviataReportDataAsync(StartDate, EndDate, selectedIds);

                if (reportData == null || !reportData.Any())
                {
                    MessageBox.Show("No data found for the selected Funding Sources.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                QuestPDF.Settings.License = LicenseType.Community;
                var document = new AviataDocument(reportData.GroupBy(x => x.Patient).ToList());
              
                string fileName = $"AviataPreview_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

                document.GeneratePdf(tempPath);
                AviataPreviewFilePath = new Uri(tempPath).AbsoluteUri;

                IsPreviewReadyAviata = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGeneratingAviata = false;
                OnPropertyChanged(nameof(IsGeneratingAviata));
                OnPropertyChanged(nameof(IsPreviewReadyAviata));
                OnPropertyChanged(nameof(AviataPreviewFilePath));
            }
        }

        private void SavePreviewToFile()
        {
            if (string.IsNullOrEmpty(AviataPreviewFilePath)) return;
            SaveFileDialog sfd = new SaveFileDialog 
            {
                Filter = "PDF Document|*.pdf",
                FileName = $"Aviata_{StartDate:MM-dd-yyyy}_{EndDate:MM-dd-yyyy}.pdf"
            };
            
            if (sfd.ShowDialog() == true)
            {
                string localPath = new Uri(AviataPreviewFilePath).LocalPath;
                System.IO.File.Copy(localPath, sfd.FileName, true);
                MessageBox.Show("Report saved!");
            }
        }

        private async Task GenerateProductionReport(object obj)
        {
            // 1. Iniciar estado de generación
            _isGenerating = true;
            GenerateButtonText = "Generating...";
            OnPropertyChanged(nameof(IsGenerating));
            OnPropertyChanged(nameof(GenerateButtonText));
            // Forzar a la UI a reevaluar el estado del botón
            CommandManager.InvalidateRequerySuggested();

            try
            {
                // 2. Obtener IDs de Funding Sources seleccionados (Multi-select)
                var selectedFsIds = AllFundingSources
                    .Where(x => x.IsSelected && x.Id != -1)
                    .Select(x => x.Id)
                    .ToList();

                var selectedRouteIds = AllVehicleRoutes.Where(x => x.IsSelected).Select(x => x.Id).ToList();
                // 3. Obtener datos del servicio usando el rango de fechas (StartDate y EndDate)
                // Se asume que has añadido 'GetProductionReportDataRangeAsync' a tu ScheduleService
                var reportData = await _scheduleService.GetProductionReportDataRangeAsync(StartDate, EndDate, selectedFsIds, selectedRouteIds);

                if (reportData == null || reportData.Count == 0)
                {
                    MessageBox.Show("No production data found for the selected date range and filters.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 4. Configurar el diálogo para guardar el archivo Excel
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Workbook|*.xlsx",
                    Title = "Save Production Report",
                    FileName = $"ProductionReport_{StartDate:yyyyMMdd}_to_{EndDate:yyyyMMdd}.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("Production Report");

                        // 5. Definir Encabezados de la tabla
                        string[] headers = new string[] {
                    "Date", "Req Pickup", "Appointment", "Patient", "Pickup Address",
                    "Dropoff Address", "Space", "Charge", "Paid", "Pickup Comment",
                    "Dropoff Comment", "Type", "Pickup Phone", "Dropoff Phone", "Authorization",
                    "Funding Source", "Distance", "Run", "Driver", "Pickup Arrive",
                    "Pickup Perform", "Dropoff Arrive", "Dropoff Perform", "Will Call", "Canceled",
                    "VIN", "Pickup Odometer", "Dropoff Odometer", "Will Call Time", "Vehicle",
                    "Vehicle Plate", "Trip Id", "Pickup GPS Arrive Distance", "Dropoff GPS Arrive Distance", "Pickup City",
                    "Pickup State", "Pickup Zip", "Dropoff City", "Dropoff State", "Dropoff Zip",
                    "Patient Address", "DOB", "Driver No-Show Reason", "Pickup Lat", "Pickup Lon",
                    "Dropoff Lat", "Dropoff Lon", "Created"
                };

                        // Estilo para el encabezado
                        for (int i = 0; i < headers.Length; i++)
                        {
                            var cell = worksheet.Cell(1, i + 1);
                            cell.Value = headers[i];
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#007ACC");
                            cell.Style.Font.FontColor = XLColor.White;
                        }

                        // 6. Llenar el Excel con los datos del DTO
                        for (int i = 0; i < reportData.Count; i++)
                        {
                            var d = reportData[i];
                            int r = i + 2; // Empezar en la fila 2 debido al encabezado

                            worksheet.Cell(r, 1).Value = d.Date.ToShortDateString();
                            worksheet.Cell(r, 2).Value = d.ReqPickup?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 3).Value = d.Appointment?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 4).Value = d.Patient;
                            worksheet.Cell(r, 5).Value = d.PickupAddress;
                            worksheet.Cell(r, 6).Value = d.DropoffAddress;
                            worksheet.Cell(r, 7).Value = d.Space;
                            worksheet.Cell(r, 8).Value = d.Charge;
                            worksheet.Cell(r, 9).Value = d.Paid;
                            worksheet.Cell(r, 10).Value = d.PickupComment;
                            worksheet.Cell(r, 11).Value = d.DropoffComment;
                            worksheet.Cell(r, 12).Value = d.Type;
                            worksheet.Cell(r, 13).Value = d.PickupPhone;
                            worksheet.Cell(r, 14).Value = d.DropoffPhone;
                            worksheet.Cell(r, 15).Value = d.Authorization;
                            worksheet.Cell(r, 16).Value = d.FundingSource;
                            worksheet.Cell(r, 17).Value = d.Distance;
                            worksheet.Cell(r, 18).Value = d.Run;
                            worksheet.Cell(r, 19).Value = d.Driver;
                            worksheet.Cell(r, 20).Value = d.PickupArrive?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 21).Value = d.PickupPerform?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 22).Value = d.DropoffArrive?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 23).Value = d.DropoffPerform?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 24).Value = d.WillCall ? "Yes" : "No";
                            worksheet.Cell(r, 25).Value = d.Canceled ? "Yes" : "No";
                            worksheet.Cell(r, 26).Value = d.VIN;
                            worksheet.Cell(r, 27).Value = d.PickupOdometer;
                            worksheet.Cell(r, 28).Value = d.DropoffOdometer;
                            worksheet.Cell(r, 29).Value = d.WillCallTime?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 30).Value = d.Vehicle;
                            worksheet.Cell(r, 31).Value = d.VehiclePlate;
                            worksheet.Cell(r, 32).Value = d.TripId;
                            worksheet.Cell(r, 33).Value = d.PickupGpsArriveDistance;
                            worksheet.Cell(r, 34).Value = d.DropoffGpsArriveDistance;
                            worksheet.Cell(r, 35).Value = d.PickupCity;
                            worksheet.Cell(r, 36).Value = d.PickupState;
                            worksheet.Cell(r, 37).Value = d.PickupZip;
                            worksheet.Cell(r, 38).Value = d.DropoffCity;
                            worksheet.Cell(r, 39).Value = d.DropoffState;
                            worksheet.Cell(r, 40).Value = d.DropoffZip;
                            worksheet.Cell(r, 41).Value = d.PatientAddress;
                            worksheet.Cell(r, 42).Value = d.DOB?.ToShortDateString() ?? "";
                            worksheet.Cell(r, 43).Value = d.DriverNoShowReason;
                            worksheet.Cell(r, 44).Value = d.PickupLat;
                            worksheet.Cell(r, 45).Value = d.PickupLon;
                            worksheet.Cell(r, 46).Value = d.DropoffLat;
                            worksheet.Cell(r, 47).Value = d.DropoffLon;
                            worksheet.Cell(r, 48).Value = d.Created.ToString("g");
                        }

                        // Ajustar columnas al contenido
                        worksheet.Columns().AdjustToContents();

                        // 7. Guardar el archivo
                        workbook.SaveAs(saveFileDialog.FileName);
                        MessageBox.Show("Production Report generated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while generating the Excel report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 8. Restaurar estado de la UI
                _isGenerating = false;
                GenerateButtonText = "Generate Production Report";
                OnPropertyChanged(nameof(IsGenerating));
                OnPropertyChanged(nameof(GenerateButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task GenerateProductionReportFullColumnOld(object obj)
        {
            _isGenerating = true;
            GenerateButtonText = "Generating...";
            CommandManager.InvalidateRequerySuggested();

            try
            {
                int? fundingSourceId = (SelectedFundingSource != null && SelectedFundingSource.Id != -1)
                    ? SelectedFundingSource.Id : (int?)null;

                var reportData = await _scheduleService.GetProductionReportDataAsync(SelectedDate, fundingSourceId);

                if (reportData == null || reportData.Count == 0)
                {
                    MessageBox.Show("No production data found.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Workbook|*.xlsx",
                    FileName = $"ProductionReport_{SelectedDate:yyyyMMdd}.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("Production Report");

                        var headers = new string[] {
                    "Date", "Req Pickup", "Appointment", "Patient", "Pickup Address",
                    "Dropoff Address", "Space", "Charge", "Paid", "Pickup Comment",
                    "Dropoff Comment", "Type", "Pickup Phone", "Dropoff Phone", "Authorization",
                    "Funding Source", "Distance", "Run", "Driver", "Pickup Arrive",
                    "Pickup Perform", "Dropoff Arrive", "Dropoff Perform", "Will Call", "Canceled",
                    "VIN", "Pickup Odometer", "Dropoff Odometer", "Will Call Time", "Vehicle",
                    "Vehicle Plate", "Trip Id", "Pickup GPS Arrive Distance", "Dropoff GPS Arrive Distance", "Pickup City",
                    "Pickup State", "Pickup Zip", "Dropoff City", "Dropoff State", "Dropoff Zip",
                    "Patient Address", "DOB", "Driver No-Show Reason", "Pickup Lat", "Pickup Lon",
                    "Dropoff Lat", "Dropoff Lon", "Created"
                };

                        for (int i = 0; i < headers.Length; i++)
                        {
                            worksheet.Cell(1, i + 1).Value = headers[i];
                        }

                        for (int i = 0; i < reportData.Count; i++)
                        {
                            var d = reportData[i];
                            var r = i + 2;

                            worksheet.Cell(r, 1).Value = d.Date.ToShortDateString();
                            worksheet.Cell(r, 2).Value = d.ReqPickup?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 3).Value = d.Appointment?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 4).Value = d.Patient;
                            worksheet.Cell(r, 5).Value = d.PickupAddress;
                            worksheet.Cell(r, 6).Value = d.DropoffAddress;
                            worksheet.Cell(r, 7).Value = d.Space;
                            worksheet.Cell(r, 8).Value = d.Charge;
                            worksheet.Cell(r, 9).Value = d.Paid;
                            worksheet.Cell(r, 10).Value = d.PickupComment;
                            worksheet.Cell(r, 11).Value = d.DropoffComment;
                            worksheet.Cell(r, 12).Value = d.Type;
                            worksheet.Cell(r, 13).Value = d.PickupPhone;
                            worksheet.Cell(r, 14).Value = d.DropoffPhone;
                            worksheet.Cell(r, 15).Value = d.Authorization;
                            worksheet.Cell(r, 16).Value = d.FundingSource;
                            worksheet.Cell(r, 17).Value = d.Distance;
                            worksheet.Cell(r, 18).Value = d.Run;
                            worksheet.Cell(r, 19).Value = d.Driver;
                            worksheet.Cell(r, 20).Value = d.PickupArrive?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 21).Value = d.PickupPerform?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 22).Value = d.DropoffArrive?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 23).Value = d.DropoffPerform?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 24).Value = d.WillCall ? "Yes" : "No";
                            worksheet.Cell(r, 25).Value = d.Canceled ? "Yes" : "No";
                            worksheet.Cell(r, 26).Value = d.VIN;
                            worksheet.Cell(r, 27).Value = d.PickupOdometer;
                            worksheet.Cell(r, 28).Value = d.DropoffOdometer;
                            worksheet.Cell(r, 29).Value = d.WillCallTime?.ToString(@"hh\:mm") ?? "";
                            worksheet.Cell(r, 30).Value = d.Vehicle;
                            worksheet.Cell(r, 31).Value = d.VehiclePlate;
                            worksheet.Cell(r, 32).Value = d.TripId;
                            worksheet.Cell(r, 33).Value = d.PickupGpsArriveDistance;
                            worksheet.Cell(r, 34).Value = d.DropoffGpsArriveDistance;
                            worksheet.Cell(r, 35).Value = d.PickupCity;
                            worksheet.Cell(r, 36).Value = d.PickupState;
                            worksheet.Cell(r, 37).Value = d.PickupZip;
                            worksheet.Cell(r, 38).Value = d.DropoffCity;
                            worksheet.Cell(r, 39).Value = d.DropoffState;
                            worksheet.Cell(r, 40).Value = d.DropoffZip;
                            worksheet.Cell(r, 41).Value = d.PatientAddress;
                            worksheet.Cell(r, 42).Value = d.DOB?.ToShortDateString() ?? "";
                            worksheet.Cell(r, 43).Value = d.DriverNoShowReason;
                            worksheet.Cell(r, 44).Value = d.PickupLat;
                            worksheet.Cell(r, 45).Value = d.PickupLon;
                            worksheet.Cell(r, 46).Value = d.DropoffLat;
                            worksheet.Cell(r, 47).Value = d.DropoffLon;
                            worksheet.Cell(r, 48).Value = d.Created.ToString("g");
                        }

                        worksheet.Columns().AdjustToContents();
                        workbook.SaveAs(saveFileDialog.FileName);
                        MessageBox.Show("Report generated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isGenerating = false;
                GenerateButtonText = "Generate Production Report";
                CommandManager.InvalidateRequerySuggested();
            }
        }
        private async Task GenerateProductionReportOld(object obj)
        {
            _isGenerating = true;
            GenerateButtonText = "Generating...";
            // This is important to update the UI, especially the button's IsEnabled state
            CommandManager.InvalidateRequerySuggested();

            try
            {
                // --- Determine the ID to send to the API ---
                // If the user selected our "[ All ]" placeholder, we send null.
                // Otherwise, we send the selected ID.
                int? fundingSourceId = (SelectedFundingSource != null && SelectedFundingSource.Id != -1)
                    ? SelectedFundingSource.Id
                    : (int?)null;

                // Fetch data from the API
                var reportData = await _scheduleService.GetProductionReportDataAsync(SelectedDate, fundingSourceId);

                if (reportData == null || reportData.Count == 0)
                {
                    MessageBox.Show("No production data found for the selected date.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Show SaveFileDialog to the user
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Workbook|*.xlsx",
                    Title = "Save Production Report",
                    FileName = $"ProductionReport_{SelectedDate:yyyyMMdd}.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    // Create and populate the Excel file
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("Production Report");

                        var headers = new List<string>
                        {
                            "Date", "Authorization", "Req Pickup", "Appointment", "Patient",
                            "Pickup City", "Run", "Space", "Pickup Arrive", "Dropoff Arrive"
                        };

                        for (int i = 0; i < headers.Count; i++)
                        {
                            worksheet.Cell(1, i + 1).Value = headers[i];
                        }

                        // Populate with the data from the API
                        for (int i = 0; i < reportData.Count; i++)
                        {
                            var rowData = reportData[i];
                            var row = i + 2; // Start from the second row
                            worksheet.Cell(row, 1).Value = rowData.Date;
                            worksheet.Cell(row, 2).Value = rowData.Authorization;
                            worksheet.Cell(row, 3).Value = rowData.ReqPickup;
                            worksheet.Cell(row, 4).Value = rowData.Appointment;
                            worksheet.Cell(row, 5).Value = rowData.Patient;
                            worksheet.Cell(row, 6).Value = rowData.PickupCity;
                            worksheet.Cell(row, 7).Value = rowData.Run;
                            worksheet.Cell(row, 8).Value = rowData.Space;
                            worksheet.Cell(row, 9).Value = rowData.PickupArrive;
                            worksheet.Cell(row, 10).Value = rowData.DropoffArrive;
                        }

                        worksheet.Columns().AdjustToContents();
                        workbook.SaveAs(saveFileDialog.FileName);

                        MessageBox.Show("Report generated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while generating the report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isGenerating = false;
                GenerateButtonText = "Generate Production Report";
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task GenerateGpsReport(object obj)
        {
            if (SelectedVehicleRoute == null)
            {
                MessageBox.Show("Please select a vehicle route.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isGeneratingGps = true;
            GenerateGpsButtonText = "Generating...";
            CommandManager.InvalidateRequerySuggested();

            try
            {
                // 1. Fetch data from the API
                var reportData = await _gpsService.GetGpsHistoryAsync(SelectedVehicleRoute.Id, SelectedDate);

                if (reportData == null || reportData.Count == 0)
                {
                    MessageBox.Show("No GPS data found for the selected date and route.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 2. Show SaveFileDialog
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Workbook|*.xlsx",
                    Title = "Save GPS Report",
                    FileName = $"GpsReport_{SelectedVehicleRoute.Name.Replace(" ", "")}_{SelectedDate:yyyyMMdd}.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    // 3. Create and populate the Excel file
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("GPS History");

                        var headers = new List<string> { "Date Time", "Speed", "Address", "Direction", "Latitude", "Longitude" };
                        for (int i = 0; i < headers.Count; i++)
                        {
                            worksheet.Cell(1, i + 1).Value = headers[i];
                        }

                        for (int i = 0; i < reportData.Count; i++)
                        {
                            var rowData = reportData[i];
                            var row = i + 2;
                            worksheet.Cell(row, 1).Value = rowData.DateTime;
                            worksheet.Cell(row, 2).Value = rowData.Speed;
                            worksheet.Cell(row, 3).Value = rowData.Address;
                            worksheet.Cell(row, 4).Value = rowData.Direction;
                            worksheet.Cell(row, 5).Value = rowData.Latitude;
                            worksheet.Cell(row, 6).Value = rowData.Longitude;
                        }

                        worksheet.Columns().AdjustToContents();
                        workbook.SaveAs(saveFileDialog.FileName);

                        MessageBox.Show("GPS report generated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while generating the GPS report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isGeneratingGps = false;
                GenerateGpsButtonText = "Generate GPS Report";
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task GenerateAviataReport(object obj)
        {
            _isGeneratingAviata = true;
            GenerateAviataButtonText = "Generating...";
            try
            {
                var reportData = await _scheduleService.GetAviataReportDataAsync(StartDate, EndDate);

                if (!reportData.Any())
                {
                    MessageBox.Show("No data found."); return;
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "PDF Document|*.pdf",
                    FileName = $"Aviata_{StartDate:MM-dd-yyyy}_{EndDate:MM-dd-yyyy}.pdf"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    QuestPDF.Settings.License = LicenseType.Community;
                    // Agrupar por Cliente
                    var groupedByClient = reportData.GroupBy(x => x.Patient).ToList();
                    var document = new AviataDocument(groupedByClient);
                    document.GeneratePdf(saveFileDialog.FileName);
                    MessageBox.Show("Report generated!");
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally
            {
                _isGeneratingAviata = false;
                GenerateAviataButtonText = "Generate Aviata Report";
            }
        }
        private async Task GenerateTrip2Report(object obj)
        {
            _isGeneratingTrip2 = true;
            GenerateTrip2ButtonText = "Generating PDF...";
            CommandManager.InvalidateRequerySuggested();

            try
            {
                var reportData = await _scheduleService.GetProductionReportDataAsync(SelectedDate, null);

                if (reportData == null || !reportData.Any())
                {
                    MessageBox.Show("No data found for this date.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "PDF Document|*.pdf",
                    FileName = $"Trip2_{SelectedDate:MM-dd-yyyy}.pdf"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    // Agrupar datos por Driver (Cada driver inicia en página nueva)
                    var groupedByDriver = reportData.GroupBy(d => d.Driver ?? "Unknown Driver").ToList();

                    // Generar el PDF usando QuestPDF                  
                    QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
                    var document = new Trip2Document(groupedByDriver, SelectedDate);
                    document.GeneratePdf(saveFileDialog.FileName);

                    MessageBox.Show("PDF Report generated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isGeneratingTrip2 = false;
                GenerateTrip2ButtonText = "Generate Trip2 PDF";
                CommandManager.InvalidateRequerySuggested();
            }
        }


    }
}