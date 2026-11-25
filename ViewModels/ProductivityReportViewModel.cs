using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using QuestPDF.Fluent;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Models.Pdf;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// RE-081: the fleet productivity report. Hours and miles per driver and day over a date range,
    /// read from the day rows RE-062 computes after each Pull-in. Office staff only (the API refuses
    /// anyone else). An empty driver or vehicle selection means all of them.
    /// </summary>
    public partial class ProductivityReportViewModel : ObservableObject
    {
        private readonly IProductivityReportService _service;
        private readonly IProductivityWarningService _warnings = new ProductivityWarningService();
        private readonly UserService _users = new();
        private readonly VehicleService _vehicles = new();
        private bool _filtersLoaded;

        public ProductivityReportViewModel() : this(new ProductivityReportService()) { }

        public ProductivityReportViewModel(IProductivityReportService service)
        {
            _service = service;
            var today = DateTime.Today;
            StartDate = today.AddDays(-7);
            EndDate = today.AddDays(-1);
            LocalizationService.Instance.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(string.Empty);
                // A row's words are made once: they follow the language only if the rows are made again.
                if (Rows.Count > 0) Show(Rows.Select(r => r.Data).ToList());
            };

            _rowsView = Grouped(Rows);

            // RE-092: the limits were changed in the legend, which stays open beside the report.
            WeakReferenceMessenger.Default.Register<ProductivityReportViewModel, WarningLimitsSaved>(this,
                (vm, saved) => { if (vm.HasRun) _ = vm.LoadWarningsAsync(); });
        }

        /// <summary>
        /// The rows grouped by driver (the order is already driver, then day). A new view per
        /// report, never a Clear() and refill of the old one: a grouped DataGrid that receives
        /// a Reset followed by one Add per row loses its column widths and headers (RE-082,
        /// every second report came out as a strip of 20-pixel columns).
        /// </summary>
        public ICollectionView RowsView
        {
            get => _rowsView;
            private set => SetProperty(ref _rowsView, value);
        }
        private ICollectionView _rowsView;

        private static ICollectionView Grouped(System.Collections.IList rows)
        {
            var view = new ListCollectionView(rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProductivityRow.Group)));
            return view;
        }

        private static string T(string key) => LocalizationService.Instance[key];

        // ------------------------------------------------------------------ texts
        public string TitleText => T("productivity.Title");
        public string IntroText => T("productivity.Intro");
        public string DateRangeText => T("productivity.DateRange");
        public string DriversText => T("productivity.Drivers");
        public string VehiclesText => T("productivity.Vehicles");
        public string AllHintText => T("productivity.AllHint");
        public string GenerateText => T("productivity.Generate");
        public string PdfText => T("productivity.SavePdf");
        public string ExcelText => T("productivity.SaveExcel");
        public string ColDate => T("productivity.ColDate");
        public string ColDriver => T("productivity.ColDriver");
        public string ColVehicle => T("productivity.ColVehicle");
        public string ColShift => T("productivity.ColShift");
        public string ColPaid => T("productivity.ColPaid");
        public string ColMiles => T("productivity.ColMiles");
        public string ColLoaded => T("productivity.ColLoaded");
        public string ColTrips => T("productivity.ColTrips");
        public string ColReliability => T("productivity.ColReliability");
        public string TotalPaidText => T("productivity.TotalPaid");
        public string TotalMilesText => T("productivity.TotalMiles");
        public string TotalLoadedText => T("productivity.TotalLoaded");
        public string TotalTripsText => T("productivity.TotalTrips");
        public string TotalDaysText => T("productivity.TotalDays");
        public string HoursBreakdownText => T("productivity.HoursBreakdown");
        public string MilesBreakdownText => T("productivity.MilesBreakdown");
        public string ColRate => T("productivity.ColRate");
        public string ColAmount => T("productivity.ColAmount");
        public string TotalAmountText => T("productivity.TotalAmount");
        public string GrandTotalText => T("productivity.GrandTotal");

        /// <summary>RE-082: amounts came back (the caller is an administrator and somebody had a rate).</summary>
        public bool HasPay => Totals.HasPay;
        public string EmptyText => !HasRun ? T("productivity.NotRun")
            : _all.Count > 0 ? string.Format(T("productivity.NoRowsTicked"), string.Join(" · ", new[] { DriversTickedText, VehiclesTickedText }.Where(t => t.Length > 0)))
            : T("productivity.NoRows");

        // What is ticked, in words: a closed list shows nothing, and a forgotten tick empties the report.
        public string ClearTicksText => T("productivity.ClearTicks");
        public bool HasDriversTicked => Drivers.Any(o => o.IsSelected);
        public bool HasVehiclesTicked => Vehicles.Any(o => o.IsSelected);
        public string DriversTickedText => Ticked(Drivers);
        public string VehiclesTickedText => Ticked(Vehicles);
        private static string Ticked(IEnumerable<ReportFilterOption> options) => string.Join(", ", options.Where(o => o.IsSelected).Select(o => o.Name));

        [RelayCommand] private void ClearDrivers() => Clear(Drivers);
        [RelayCommand] private void ClearVehicles() => Clear(Vehicles);

        private bool _clearing;
        private void Clear(IEnumerable<ReportFilterOption> options)
        {
            _clearing = true;
            try { foreach (var o in options) o.IsSelected = false; }
            finally { _clearing = false; }
            OnTicksChanged();
        }

        private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ReportFilterOption.IsSelected) && !_clearing) OnTicksChanged();
        }

        private void OnTicksChanged()
        {
            OnPropertyChanged(nameof(HasDriversTicked));
            OnPropertyChanged(nameof(HasVehiclesTicked));
            OnPropertyChanged(nameof(DriversTickedText));
            OnPropertyChanged(nameof(VehiclesTickedText));
            if (HasRun) Show(Filtered());
        }

        // ------------------------------------------------------------------ filters
        [ObservableProperty] private DateTime _startDate;
        [ObservableProperty] private DateTime _endDate;

        public ObservableCollection<ReportFilterOption> Drivers { get; } = new();
        public ObservableCollection<ReportFilterOption> Vehicles { get; } = new();

        /// <summary>Loaded the first time the tab is shown, not with the Reports screen.</summary>
        public async Task EnsureFiltersAsync()
        {
            if (_filtersLoaded) return;
            _filtersLoaded = true;
            try
            {
                foreach (var u in (await _users.GetAllAsync()).Where(u => u.RoleId == 2).OrderBy(u => u.FullName))
                    Drivers.Add(new ReportFilterOption(u.Id, u.FullName));
                foreach (var v in (await _vehicles.GetVehiclesAsync()).OrderBy(v => v.Name))
                    Vehicles.Add(new ReportFilterOption(v.Id, v.Name));
                foreach (var o in Drivers.Concat(Vehicles)) o.PropertyChanged += OnOptionChanged;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Productivity report: could not load the filters. {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ result
        public ObservableCollection<ProductivityRow> Rows { get; private set; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasRows), nameof(EmptyText))]
        [NotifyCanExecuteChangedFor(nameof(SavePdfCommand), nameof(SaveExcelCommand))]
        private bool _hasRun;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
        private bool _isBusy;

        [ObservableProperty] private string _errorText = string.Empty;

        public bool HasRows => Rows.Count > 0;

        /// <summary>The period at a glance: one card per figure, each with its explanation.</summary>
        public IReadOnlyList<ReportTile> Tiles { get; private set; } = Array.Empty<ReportTile>();
        public int TileColumns => Math.Max(1, Tiles.Count);

        public ProductivityTotals Totals { get; private set; } = ProductivityTotals.Of(Array.Empty<ProductivityReportRowDto>());

        [RelayCommand(CanExecute = nameof(CanGenerate))]
        private async Task GenerateAsync()
        {
            ErrorText = string.Empty;
            if (EndDate.Date < StartDate.Date) { ErrorText = T("productivity.BadRange"); return; }

            IsBusy = true;
            try
            {
                // Every driver and vehicle of the range, filtered here: a tick answers at once, and the
                // lists can say who has nothing in these dates.
                _all = await _service.GetAsync(StartDate, EndDate, Array.Empty<int>(), Array.Empty<int>());
                MarkData(Drivers, _all.Select(r => r.DriverId));
                MarkData(Vehicles, _all.Select(r => r.VehicleId));
                HasRun = true;
                _days = null;
                Show(Filtered());
                _ = LoadWarningsAsync();   // after the report is on screen, which never waits for them
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Productivity report: {ex.Message}");
                ErrorText = T("productivity.LoadError");
            }
            finally { IsBusy = false; }
        }

        private List<ProductivityReportRowDto> _all = new();

        // ------------------------------------------------------------------ RE-092: warnings
        private List<ProductivityDayWarningsDto>? _days;
        private int _warningsRun;

        [ObservableProperty] private string _warningsError = string.Empty;

        public string LegendText => T("warn.Legend");

        /// <summary>Shifts with no Pull-in: they have no row, so they go above the list.</summary>
        public IReadOnlyList<string> OpenShifts { get; private set; } = Array.Empty<string>();
        public bool HasOpenShifts => OpenShifts.Count > 0;
        public string OpenShiftsTitle => OpenShifts.Count == 1 ? T("warn.OpenTitleOne") : string.Format(T("warn.OpenTitle"), OpenShifts.Count);
        public string OpenShiftsHint => T("warn.OpenHint");

        private async Task LoadWarningsAsync()
        {
            var run = ++_warningsRun;
            WarningsError = string.Empty;
            try
            {
                var days = await _warnings.ListAsync(StartDate, EndDate);
                if (run != _warningsRun) return;   // another report was asked for meanwhile
                _days = days;
                ApplyWarnings();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Productivity warnings: {ex.Message}");
                if (run == _warningsRun) WarningsError = T("warn.LoadError");
            }
        }

        private void ApplyWarnings()
        {
            var byDay = _days?.Where(d => !d.IsOpenShift).ToDictionary(d => (d.VehicleRouteId, d.OperatingDate.Date));
            foreach (var row in Rows)
                if (byDay != null)
                {
                    var data = row.Data;
                    Task<ProductivityDayWarningsDto?>? detail = null;
                    // One call per day, however many of its warnings are opened; a failed one is asked again.
                    Task<ProductivityDayWarningsDto?> Load() => detail is null || detail.IsFaulted ? detail = _warnings.DetailAsync(data.VehicleRouteId, data.OperatingDate) : detail;
                    var found = byDay.GetValueOrDefault((data.VehicleRouteId, data.OperatingDate.Date));
                    row.SetWarnings((found?.Warnings ?? new()).OrderBy(w => WarningLook.Rank(w.Level))
                        .Select(w => new ProductivityWarningItem(w, data.VehicleId, Load)).ToList(), Load);
                }

            var drivers = Selected(Drivers);
            var vehicles = Selected(Vehicles);
            OpenShifts = (_days ?? new()).Where(d => d.IsOpenShift
                    && (drivers.Count == 0 || (d.DriverId is { } a && drivers.Contains(a)))
                    && (vehicles.Count == 0 || (d.VehicleId is { } b && vehicles.Contains(b))))
                .Select(d => string.Format(T("warn.OpenLine"), d.OperatingDate.ToString("MM/dd/yyyy"), d.DriverName ?? "—", d.VehicleName ?? d.RouteName ?? "—",
                    WarningLook.N(d.Warnings.FirstOrDefault()?.Value))).ToList();
            OnPropertyChanged(nameof(OpenShifts));
            OnPropertyChanged(nameof(HasOpenShifts));
            OnPropertyChanged(nameof(OpenShiftsTitle));
        }

        [RelayCommand]
        private void OpenLegend(string? code) => Views.Reports.WarningLegendWindow.ShowLegend(code);

        [RelayCommand]
        private async Task CorrectOdometerAsync(ProductivityWarningItem? item)
        {
            if (item?.Data.CorrectScheduleId is not int id) return;
            await item.EnsureAsync();
            if (item.FirstCase is not { } c) { item.IsOpen = true; return; }
            var isIn = c.Action == "Pull-in";
            if ((isIn ? c.OdometerIn : c.OdometerOut) is not long current) return;

            var row = Rows.FirstOrDefault(r => r.WarningItems.Contains(item));
            var target = new OdometerTarget(id, $"{c.Action} · {row?.Date:MM/dd/yyyy} · {row?.Driver} · {row?.Data.VehicleName}", isIn, current,
                isIn ? c.OdometerOut : c.OdometerIn, c.GpsMiles is > 1 ? c.GpsMiles : c.PlannedMiles is > 1 ? c.PlannedMiles : null, isIn ? null : c.PreviousReading);
            if (Views.Reports.OdometerCorrectionWindow.Show(target)) await GenerateAsync();
        }

        [RelayCommand]
        private async Task OpenOdometerHistoryAsync(ProductivityWarningItem? item)
        {
            if (item?.VehicleId is not int id) return;
            var row = Rows.FirstOrDefault(r => r.WarningItems.Contains(item));
            if (Views.Reports.OdometerHistoryWindow.Show(id, row?.Data.VehicleName)) await GenerateAsync();
        }

        private List<ProductivityReportRowDto> Filtered()
        {
            var drivers = Selected(Drivers);
            var vehicles = Selected(Vehicles);
            return _all.Where(r => (drivers.Count == 0 || (r.DriverId is { } d && drivers.Contains(d)))
                                && (vehicles.Count == 0 || (r.VehicleId is { } v && vehicles.Contains(v)))).ToList();
        }

        /// <summary>Who has days in the range goes first; the rest stays below, dimmed.</summary>
        private static void MarkData(ObservableCollection<ReportFilterOption> options, IEnumerable<int?> withData)
        {
            var ids = withData.OfType<int>().ToHashSet();
            foreach (var o in options) o.HasData = ids.Contains(o.Id);
            var ordered = options.OrderByDescending(o => o.HasData).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (var i = 0; i < ordered.Count; i++) options.Move(options.IndexOf(ordered[i]), i);
        }

        private void Show(List<ProductivityReportRowDto> data)
        {
            Rows = new ObservableCollection<ProductivityRow>(ProductivityRow.Grouped(data));
            RowsView = Grouped(Rows);
            ApplyWarnings();
            Totals = ProductivityTotals.Of(data);
            OnPropertyChanged(nameof(Totals));
            Tiles = Totals.Tiles();
            OnPropertyChanged(nameof(Tiles));
            OnPropertyChanged(nameof(TileColumns));
            OnPropertyChanged(nameof(HasPay));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(EmptyText));
            SavePdfCommand.NotifyCanExecuteChanged();
            SaveExcelCommand.NotifyCanExecuteChanged();
        }

        private bool CanGenerate() => !IsBusy;
        private bool CanSave() => HasRows;

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void SavePdf()
        {
            var path = AskPath("pdf", T("productivity.PdfFilter"));
            if (path is null) return;
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var (rows, from, to) = (Rows.Select(r => r.Data).ToList(), StartDate, EndDate);
            Views.Reports.SaveFileOverlay.Run(path, file => new ProductivityDocument(rows, from, to).GeneratePdf(file));
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void SaveExcel()
        {
            var path = AskPath("xlsx", T("productivity.ExcelFilter"));
            if (path is null) return;
            var (rows, from, to) = (Rows.Select(r => r.Data).ToList(), StartDate, EndDate);
            Views.Reports.SaveFileOverlay.Run(path, file => ProductivityWorkbook.Save(rows, from, to, file));
        }

        private string? AskPath(string ext, string filter)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"Productivity_{StartDate:yyyy-MM-dd}_{EndDate:yyyy-MM-dd}.{ext}",
                Filter = filter,
                DefaultExt = ext
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static List<int> Selected(IEnumerable<ReportFilterOption> options) => options.Where(o => o.IsSelected).Select(o => o.Id).ToList();
    }

    /// <summary>One driver's day, ready to show: the figures and their wording.</summary>
    public partial class ProductivityRow : ObservableObject
    {
        public ProductivityRow(ProductivityReportRowDto data, DriverGroup? group = null)
        {
            Data = data;
            Group = group ?? new DriverGroup(data.DriverId, data.DriverName ?? "—", new[] { data });
        }

        public ProductivityReportRowDto Data { get; }

        /// <summary>The driver's block this day belongs to: the grid groups by it and shows its subtotal.</summary>
        public DriverGroup Group { get; }

        /// <summary>Driver by name, then day: the order of the grid, the PDF and the workbook.</summary>
        public static List<ProductivityRow> Grouped(IEnumerable<ProductivityReportRowDto> data)
        {
            var list = new List<ProductivityRow>();
            foreach (var g in data.GroupBy(d => (d.DriverId, Name: d.DriverName ?? "—")).OrderBy(g => g.Key.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var days = g.OrderBy(d => d.OperatingDate).ThenBy(d => d.RouteName).ToList();
                var group = new DriverGroup(g.Key.DriverId, g.Key.Name, days);
                list.AddRange(days.Select(d => new ProductivityRow(d, group)));
            }
            return list;
        }

        // ---- RE-082: pay ----
        public bool HasPay => Data.PayAmount is not null;
        public string RateText => ProductivityFormat.Rate(Data.PayRateType, Data.PayRate);
        public string AmountText => ProductivityFormat.Money(Data.PayAmount);
        public string PayTip => Data.PayRateType is null ? T("productivity.NoRateTip")
            : Data.PayRateType == DriverPayRateType.Hourly
                ? $"{ProductivityFormat.Hours(Data.PaidMinutes)} h × {ProductivityFormat.Money(Data.PayRate)} = {AmountText}"
                : $"{Data.PayUnits:N1} mi × {ProductivityFormat.Money(Data.PayRate)} = {AmountText}";

        private static string T(string key) => LocalizationService.Instance[key];

        public DateTime Date => Data.OperatingDate;
        public string Driver => Data.DriverName ?? "—";
        public string Vehicle => string.IsNullOrEmpty(Data.VehicleName) ? (Data.RouteName ?? "—") : $"{Data.VehicleName} · {Data.RouteName}";

        /// <summary>The paid shift in the viewer's clock (the instants are UTC).</summary>
        public string ShiftText => Data.PaidStartUtc is { } s && Data.PaidEndUtc is { } e ? $"{Local(s):HH:mm}–{Local(e):HH:mm}" : "—";
        public string PaidText => ProductivityFormat.Hours(Data.PaidMinutes);
        public string MilesText => ProductivityFormat.Miles(Data.VehicleMiles);
        public string LoadedText => Data.VehicleMiles is > 0 && Data.LoadedMiles is { } l ? $"{l / Data.VehicleMiles.Value:P0}" : "—";
        public int Trips => Data.TripsCompleted;

        /// <summary>A traffic light that grades the figures, never changes them (spec §5.5).</summary>
        public string Reliability => WarningItems.Count > 0
            ? WarningItems.Count == 1 ? T("warn.ChipOne") : string.Format(T("warn.ChipMany"), WarningItems.Count)
            : Issues.Count == 0 ? "OK" : Issues.Count == 1 ? "!" : "!!";
        public bool IsReliable => WarningItems.Count == 0 && Issues.Count == 0;
        public string ReliabilityTip => IsReliable ? T("productivity.ReliableTip")
            : string.Join("\n", WarningItems.Count > 0 ? WarningItems.Select(w => w.Badge.Length > 0 ? $"{w.Title} · {w.Badge}" : w.Title) : Issues);

        /// <summary>"3 warnings · 11 cases": how many, and how many times in all.</summary>
        public string ReliabilitySummary
        {
            get
            {
                if (WarningItems.Count == 0) return "";
                var cases = WarningItems.Sum(w => w.Data.Count);
                var casesText = cases == 1 ? T("warn.CaseOne") : string.Format(T("warn.CaseMany"), cases);
                return WarningItems.Count == 1 ? string.Format(T("warn.SummaryOne"), casesText) : string.Format(T("warn.SummaryMany"), WarningItems.Count, casesText);
            }
        }

        // RE-092: the worst of the day's warnings colours its chip.
        private string Worst => WarningItems.Count == 0 ? (IsReliable ? "ok" : "review") : WarningItems.OrderBy(w => WarningLook.Rank(w.Data.Level)).First().Data.Level;
        public Brush ChipFill => Worst == "ok" ? Brush("#DCF2E3") : WarningLook.Fill(Worst);
        public Brush ChipInk => Worst == "ok" ? Brush("#1B7F3B") : WarningLook.Ink(Worst);
        public string ChipIcon => Worst == "ok" ? "CheckCircle" : WarningLook.Icon(Worst);

        private bool _fromServer;

        // ---- RE-092: the day's calculations, asked for when they are opened ----
        private Func<Task<ProductivityDayWarningsDto?>>? _load;
        public bool CanCalculate => _load is not null;
        public string CalcText => T("calc.Button");
        public string ShiftCalcTip => T("calc.ShiftTip");
        public string MilesCalcTip => T("calc.MilesTip");
        public Task<ProductivityDayWarningsDto?> DetailAsync() => _load?.Invoke() ?? Task.FromResult<ProductivityDayWarningsDto?>(null);

        [RelayCommand] private void OpenShiftCalculation() => Views.Reports.DayCalculationWindow.ShowHours(this);
        [RelayCommand] private void OpenMilesCalculation() => Views.Reports.DayCalculationWindow.ShowMiles(this);

        /// <summary>The server's warnings, with their cases. Empty until they come, and for a day with none.</summary>
        public IReadOnlyList<ProductivityWarningItem> WarningItems { get; private set; } = Array.Empty<ProductivityWarningItem>();

        public void SetWarnings(IReadOnlyList<ProductivityWarningItem> items, Func<Task<ProductivityDayWarningsDto?>> load)
        {
            WarningItems = items;
            _load = load;
            _fromServer = true;
            OnPropertyChanged(string.Empty);
        }

        private List<string> Issues
        {
            get
            {
                // Only against a server older than RE-092, which gives no warnings of its own.
                var list = new List<string>();
                if (_fromServer) return list;
                if (Data.SuspectMarks > 0) list.Add(string.Format(T("productivity.IssueMarks"), Data.SuspectMarks));
                if (Data.StartTrimmedMinutes + Data.EndTrimmedMinutes > 0)
                    list.Add(string.Format(T("productivity.IssueTrimmed"), ProductivityFormat.Hours(Data.StartTrimmedMinutes + Data.EndTrimmedMinutes)));
                if (Data.OffRouteMiles is > 100) list.Add(string.Format(T("productivity.IssueOffRoute"), Data.OffRouteMiles));
                if (Data.GpsMiles is { } g && Data.VehicleMiles is > 0 && Math.Abs(g - Data.VehicleMiles.Value) / Data.VehicleMiles.Value > 0.15)
                    list.Add(T("productivity.IssueGps"));
                return list;
            }
        }

        // The day, opened: the paid shift's buckets and the three mile totals.
        public string Loaded => ProductivityFormat.Hours(Data.LoadedMinutes);
        public string Inspection => ProductivityFormat.Hours(Data.InspectionMinutes);
        public string ScheduleWait => ProductivityFormat.Hours(Data.ScheduleWaitMinutes);
        public string PickupWait => ProductivityFormat.Hours(Data.PickupWaitMinutes);
        public string DeadheadDrive => ProductivityFormat.Hours(Data.DeadheadDriveMinutes);
        public string Other => ProductivityFormat.Hours(Data.OtherMinutes);
        public string PatientHours => ProductivityFormat.Hours(Data.PatientMinutes);
        public string RealShift => Data.ShiftStartUtc is { } s && Data.ShiftEndUtc is { } e
            ? $"{Local(s):HH:mm}–{Local(e):HH:mm} ({ProductivityFormat.Hours(Data.ShiftMinutes)})" : ProductivityFormat.Hours(Data.ShiftMinutes);
        public string Trimmed => ProductivityFormat.Hours(Data.StartTrimmedMinutes + Data.EndTrimmedMinutes);

        public string Odometer => Data.OdometerOut is { } o && Data.OdometerIn is { } i ? $"{o:N0} → {i:N0} = {ProductivityFormat.Miles(Data.VehicleMiles)}" : ProductivityFormat.Miles(Data.VehicleMiles);
        public string LoadedMiles => ProductivityFormat.Miles(Data.LoadedMiles);
        public string DeadheadMiles => ProductivityFormat.Miles(Data.DeadheadMiles);
        public string GpsMiles => ProductivityFormat.Miles(Data.GpsMiles);
        public string PlannedMiles => ProductivityFormat.Miles(Data.PlannedMiles);
        public string PassengerMiles => ProductivityFormat.Miles(Data.PassengerMiles);
        public string OffRouteMiles => Data.OffRouteMiles is { } m ? $"{m:N0} mi" : "—";

        public string HoursTitle => T("productivity.HoursBreakdown");
        public string MilesTitle => T("productivity.MilesBreakdown");

        /// <summary>The six buckets add up to the paid shift; the real shift and patient-hours come after, apart.</summary>
        public IReadOnlyList<DetailItem> HoursItems => new[]
        {
            new DetailItem(T("productivity.HLoaded"), Loaded),
            new DetailItem(T("productivity.HInspection"), Inspection),
            new DetailItem(T("productivity.XScheduleWait"), ScheduleWait),
            new DetailItem(T("productivity.XPickupWait"), PickupWait),
            new DetailItem(T("productivity.HDeadhead"), DeadheadDrive),
            new DetailItem(T("productivity.HOther"), Other),
            new DetailItem(T("productivity.ColPaid"), PaidText),
            new DetailItem(T("productivity.DRealShift"), RealShift),
            new DetailItem(T("productivity.DTrimmed"), Trimmed),
            new DetailItem(T("productivity.DPatientHours"), PatientHours),
        };

        public IReadOnlyList<DetailItem> MilesItems => new[]
        {
            new DetailItem(T("productivity.ColMiles"), Odometer),
            new DetailItem(T("productivity.MLoaded"), LoadedMiles),
            new DetailItem(T("productivity.MDeadhead"), DeadheadMiles),
            new DetailItem(T("productivity.XGps"), GpsMiles),
            new DetailItem(T("productivity.XPlanned"), PlannedMiles),
            new DetailItem(T("productivity.XPassenger"), PassengerMiles),
            new DetailItem(T("productivity.XOffRoute"), OffRouteMiles),
        };

        // ---------------------------------------------------------------- the day, drawn
        /// <summary>Width of the bars in the breakdown, in pixels.</summary>
        public const double BarWidth = 360;

        /// <summary>The paid shift as a stacked bar: one segment per bucket, in the order they are counted.</summary>
        public IReadOnlyList<DaySegment> HourSegments
        {
            get
            {
                var parts = new (string key, int minutes, string color, string info)[]
                {
                    ("productivity.HLoaded", Data.LoadedMinutes, "#673AB7", "h.loaded"),
                    ("productivity.HInspection", Data.InspectionMinutes, "#0EA5E9", "h.inspection"),
                    ("productivity.XScheduleWait", Data.ScheduleWaitMinutes, "#F59E0B", "h.hourwait"),
                    ("productivity.XPickupWait", Data.PickupWaitMinutes, "#FB7185", "h.patientwait"),
                    ("productivity.HDeadhead", Data.DeadheadDriveMinutes, "#64748B", "h.empty"),
                    ("productivity.HOther", Data.OtherMinutes, "#CBD5E1", "h.other"),
                };
                var total = Math.Max(1, parts.Sum(x => x.minutes));
                return parts.Select(x => new DaySegment(T(x.key), ProductivityFormat.Hours(x.minutes),
                    $"{(double)x.minutes / total:P0}", Brush(x.color), BarWidth * x.minutes / total, x.info,
                    string.Format(T("info.Ex.Share"), ProductivityFormat.Hours(x.minutes), PaidText, $"{(double)x.minutes / total:P0}"))).ToList();
            }
        }

        /// <summary>Loaded and empty miles as one bar; the odometer is the whole.</summary>
        public IReadOnlyList<DaySegment> MileSegments
        {
            get
            {
                var loaded = Data.LoadedMiles ?? 0; var empty = Data.DeadheadMiles ?? 0; var total = loaded + empty;
                if (total <= 0) return Array.Empty<DaySegment>();
                return new[]
                {
                    new DaySegment(T("productivity.MLoaded"), ProductivityFormat.Miles(Data.LoadedMiles), $"{loaded / total:P0}", Brush("#673AB7"), BarWidth * loaded / total, "m.loaded", string.Format(T("info.Ex.Occupied"), LoadedMiles, MilesText, $"{loaded / total:P0}")),
                    new DaySegment(T("productivity.MDeadhead"), ProductivityFormat.Miles(Data.DeadheadMiles), $"{empty / total:P0}", Brush("#CBD5E1"), BarWidth * empty / total, "m.empty", string.Format(T("info.Ex.Empty"), WarningLook.N(total), WarningLook.N(loaded), WarningLook.N(empty))),
                };
            }
        }

        /// <summary>The figures beside the paid shift, in the same cards as the miles'.</summary>
        public IReadOnlyList<DetailItem> HourFacts => new[]
        {
            new DetailItem(T("productivity.DRealShift"), ProductivityFormat.Hours(Data.ShiftMinutes), "h.real", RealExample),
            new DetailItem(T("productivity.XTrimmed"), Trimmed, "h.unpaid", UnpaidExample, HasTrimmed ? "#B3261E" : "#1E2A36"),
            new DetailItem(T("productivity.XPatientHours"), PatientHours, "h.patienthours"),
            new DetailItem(T("productivity.ColTrips"), $"{Data.TripsCompleted}", "h.trips"),
        };

        public IReadOnlyList<DetailItem> MileFacts => new[]
        {
            new DetailItem(T("productivity.XPassenger"), PassengerMiles, "m.passenger", PassengerExample, "#673AB7"),
            new DetailItem(T("productivity.XGps"), GpsMiles, "m.gps"),
            new DetailItem(T("productivity.XPlanned"), PlannedMiles, "m.planned"),
            new DetailItem(T("productivity.XOffRoute"), OffRouteMiles, "m.offroute"),
        };

        public string OdometerRange => Data.OdometerOut is { } o && Data.OdometerIn is { } i ? $"{o:N0} → {i:N0}" : T("productivity.NoOdometer");
        // ---- RE-092: each figure's own numbers, for its explanation ----
        public string ShiftExample => Data.PayRateType == DriverPayRateType.Hourly
            ? string.Format(T("info.Ex.Shift"), PaidStartText, PaidEndText, PaidText) + "\n" + T("info.Ex.DayPay") + ": " + string.Format(T("info.Ex.PayHour"), PaidText, ProductivityFormat.Money(Data.PayRate), AmountText)
            : string.Format(T("info.Ex.Shift"), PaidStartText, PaidEndText, PaidText) + (Data.PayRateType is null ? "" : "\n" + T("info.Ex.NotHour"));
        public string RealExample => Data.ShiftStartUtc is { } rs && Data.ShiftEndUtc is { } re
            ? string.Format(T("info.Ex.Real"), $"{Local(rs):HH:mm}", $"{Local(re):HH:mm}", ProductivityFormat.Hours(Data.ShiftMinutes)) : "";
        public string UnpaidExample => string.Format(T("info.Ex.Unpaid"), ProductivityFormat.Hours(Data.ShiftMinutes), PaidText, Trimmed);
        public string OdometerExample => Data.OdometerOut is { } oo && Data.OdometerIn is { } oi
            ? string.Format(T("info.Ex.Odo"), $"{oi:N0}", $"{oo:N0}", WarningLook.N(Data.VehicleMiles)) : "";
        public string MilesExample => Data.VehicleMiles is null ? "" : $"{MilesText} = {LoadedMiles} + {DeadheadMiles}";
        public string PassengerExample => Data.PayRateType == DriverPayRateType.PerMile
            ? T("info.Ex.DayPay") + ": " + string.Format(T("info.Ex.PayMile"), WarningLook.N(Data.PassengerMiles), ProductivityFormat.Money(Data.PayRate), AmountText)
            : Data.PayRateType is null ? "" : T("info.Ex.NotMile");

        public string PaidStartText => Data.PaidStartUtc is { } s ? $"{Local(s):HH:mm}" : "—";
        public string PaidEndText => Data.PaidEndUtc is { } e ? $"{Local(e):HH:mm}" : "—";
        public string RealShiftLine => string.Format(T("productivity.RealShiftLine"), RealShift);
        public string TrimmedLine => string.Format(T("productivity.TrimmedLine"), Trimmed);
        public bool HasTrimmed => Data.StartTrimmedMinutes + Data.EndTrimmedMinutes > 0;
        public string PatientHoursLine => string.Format(T("productivity.PatientHoursLine"), PatientHours);
        public string TripsLine => string.Format(T("productivity.TripsLine"), Data.TripsCompleted);
        public IReadOnlyList<string> IssueLines => Issues;
        public string ReliableLine => T("productivity.ReliableTip");
        public string ReliabilityTitle => T("productivity.ColReliability");
        public string PaidFromLabel => T("productivity.PaidFrom");
        public string PaidToLabel => T("productivity.PaidTo");

        private static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);
    }

    public record DetailItem(string Label, string Value, string Info = "", string Example = "", string Ink = "#1E2A36");

    /// <summary>One segment of a breakdown bar and its legend line.</summary>
    public record DaySegment(string Label, string Value, string Share, Brush Color, double Width, string Info = "", string Example = "");

    /// <summary>One card of the period: an icon, a figure and up to two lines under it.</summary>
    public record ReportTile(string Key, string Icon, Brush Ink, Brush Fill, string Label, string Value, string Line1, string Line2, string Example, bool IsPay = false);

    /// <summary>RE-082: one driver's block in the report, and its subtotal.</summary>
    public class DriverGroup
    {
        public DriverGroup(int? driverId, string title, IReadOnlyCollection<ProductivityReportRowDto> days)
        {
            DriverId = driverId;
            Title = title;
            Days = days.Count;
            PaidMinutes = days.Sum(d => d.PaidMinutes);
            PassengerMiles = days.Sum(d => d.PassengerMiles ?? 0);
            HasPay = days.Any(d => d.PayAmount is not null);
            Amount = days.Sum(d => d.PayAmount ?? 0);
            RateText = string.Join(" · ", days.Where(d => d.PayRateType != null)
                .Select(d => ProductivityFormat.Rate(d.PayRateType, d.PayRate)).Distinct());
        }

        public int? DriverId { get; }
        public string Title { get; }
        public int Days { get; }
        public int PaidMinutes { get; }
        public double PassengerMiles { get; }
        public bool HasPay { get; }
        public decimal Amount { get; }

        /// <summary>The rate or rates the block was paid at ("$15.00/h", or two when it changed mid-range).</summary>
        public string RateText { get; }
        public string AmountText => ProductivityFormat.Money(Amount);
        public string Line => string.Format(LocalizationService.Instance["productivity.GroupLine"], Days, ProductivityFormat.Hours(PaidMinutes), $"{PassengerMiles:N1}");
        public string SubtotalLabel => LocalizationService.Instance["productivity.Subtotal"];

        public override string ToString() => Title;
    }

    public static class ProductivityFormat
    {
        private static readonly System.Globalization.CultureInfo Usd = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        public static string Money(decimal? amount) => amount is { } a ? a.ToString("C2", Usd) : "—";
        public static string Rate(DriverPayRateType? type, decimal? rate) => type is null || rate is null ? "—"
            : type == DriverPayRateType.Hourly ? $"{Money(rate)}/h" : $"{Money(rate)}/mi";
        public static string Hours(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
        public static string Miles(double? miles) => miles is { } m ? $"{m:N1} mi" : "—";
    }

    /// <summary>The period's totals, for the tiles, the PDF and the workbook.</summary>
    public class ProductivityTotals
    {
        public int Days { get; init; }
        public int Drivers { get; init; }
        public int PaidMinutes { get; init; }
        public double VehicleMiles { get; init; }
        public double LoadedMiles { get; init; }
        public double DeadheadMiles { get; init; }
        public double PassengerMiles { get; init; }
        public int Trips { get; init; }
        public bool HasPay { get; init; }
        public decimal Amount { get; init; }
        public decimal HourlyAmount { get; init; }
        public decimal PerMileAmount { get; init; }

        public string AmountText => ProductivityFormat.Money(Amount);
        public string AmountSub => string.Format(LocalizationService.Instance["productivity.SubAmount"], ProductivityFormat.Money(HourlyAmount), ProductivityFormat.Money(PerMileAmount));

        public string PaidText => ProductivityFormat.Hours(PaidMinutes);
        public string MilesText => $"{VehicleMiles:N1} mi";
        public string LoadedText => VehicleMiles > 0 ? $"{LoadedMiles / VehicleMiles:P0}" : "—";

        private static string T(string key) => LocalizationService.Instance[key];
        public string DaysSub => string.Format(T("productivity.SubDays"), Drivers);
        public string PaidSub => Days > 0 ? string.Format(T("productivity.SubPerDay"), ProductivityFormat.Hours(PaidMinutes / Days)) : string.Empty;
        public string MilesSub => Days > 0 ? string.Format(T("productivity.SubPerDay"), $"{VehicleMiles / Days:N1} mi") : string.Empty;
        public string TripsSub => Days > 0 ? string.Format(T("productivity.SubPerDay"), $"{(double)Trips / Days:N1}") : string.Empty;

        public IReadOnlyList<ReportTile> Tiles()
        {
            string Per(string total, string per) => Days > 0 ? string.Format(T("info.Ex.PerDay"), total, Days, per) : "";
            var tiles = new List<ReportTile>
            {
                new("t.days", "CalendarCheckOutline", WarningLook.Of("#673AB7"), WarningLook.Of("#EDE7F6"), T("productivity.TotalDays"), $"{Days:N0}", DaysSub, "", ""),
                new("t.paid", "ClockTimeFourOutline", WarningLook.Of("#0284C7"), WarningLook.Of("#E0F2FE"), T("productivity.TotalPaid"), PaidText, PaidSub, "", Per(PaidText, ProductivityFormat.Hours(Days > 0 ? PaidMinutes / Days : 0))),
                new("t.miles", "Speedometer", WarningLook.Of("#2E7D32"), WarningLook.Of("#E8F5E9"), T("productivity.TotalMiles"), MilesText, MilesSub, "", Per(MilesText, $"{(Days > 0 ? VehicleMiles / Days : 0):N1} mi")),
                new("t.loaded", "AccountArrowRightOutline", WarningLook.Of("#C2410C"), WarningLook.Of("#FFEDD5"), T("productivity.TotalLoaded"), $"{PassengerMiles:N1} mi",
                    string.Format(T("productivity.SubLoaded1"), LoadedText), "", VehicleMiles > 0 ? string.Format(T("info.Ex.Occupied"), $"{LoadedMiles:N1} mi", MilesText, LoadedText) : ""),
                new("t.trips", "MapMarkerPath", WarningLook.Of("#1E3A8A"), WarningLook.Of("#E0E7FF"), T("productivity.TotalTrips"), $"{Trips:N0}", TripsSub, "", "")
            };
            if (HasPay)
                tiles.Add(new("t.pay", "CashMultiple", WarningLook.Of("#673AB7"), WarningLook.Of("#EDE7F6"), T("productivity.TotalAmount"), AmountText,
                    string.Format(T("productivity.SubAmount1"), ProductivityFormat.Money(HourlyAmount)), string.Format(T("productivity.SubAmount2"), ProductivityFormat.Money(PerMileAmount)),
                    string.Format(T("info.Ex.Total"), ProductivityFormat.Money(HourlyAmount), ProductivityFormat.Money(PerMileAmount), AmountText), true));
            return tiles;
        }

        public static ProductivityTotals Of(IReadOnlyCollection<ProductivityReportRowDto> rows) => new()
        {
            Days = rows.Count,
            Drivers = rows.Select(r => r.DriverId).Distinct().Count(),
            PaidMinutes = rows.Sum(r => r.PaidMinutes),
            VehicleMiles = rows.Sum(r => r.VehicleMiles ?? 0),
            LoadedMiles = rows.Sum(r => r.LoadedMiles ?? 0),
            DeadheadMiles = rows.Sum(r => r.DeadheadMiles ?? 0),
            PassengerMiles = rows.Sum(r => r.PassengerMiles ?? 0),
            Trips = rows.Sum(r => r.TripsCompleted),
            HasPay = rows.Any(r => r.PayAmount is not null),
            Amount = rows.Sum(r => r.PayAmount ?? 0),
            HourlyAmount = rows.Where(r => r.PayRateType == DriverPayRateType.Hourly).Sum(r => r.PayAmount ?? 0),
            PerMileAmount = rows.Where(r => r.PayRateType == DriverPayRateType.PerMile).Sum(r => r.PayAmount ?? 0)
        };
    }
}
