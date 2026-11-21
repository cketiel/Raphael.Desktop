using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuestPDF.Fluent;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Models.Pdf;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            LocalizationService.Instance.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
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
        public string EmptyText => HasRun ? T("productivity.NoRows") : T("productivity.NotRun");

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
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Productivity report: could not load the filters. {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ result
        public ObservableCollection<ProductivityRow> Rows { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasRows), nameof(EmptyText))]
        [NotifyCanExecuteChangedFor(nameof(SavePdfCommand), nameof(SaveExcelCommand))]
        private bool _hasRun;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
        private bool _isBusy;

        [ObservableProperty] private string _errorText = string.Empty;

        public bool HasRows => Rows.Count > 0;

        public ProductivityTotals Totals { get; private set; } = ProductivityTotals.Of(Array.Empty<ProductivityReportRowDto>());

        [RelayCommand(CanExecute = nameof(CanGenerate))]
        private async Task GenerateAsync()
        {
            ErrorText = string.Empty;
            if (EndDate.Date < StartDate.Date) { ErrorText = T("productivity.BadRange"); return; }

            IsBusy = true;
            try
            {
                var data = await _service.GetAsync(StartDate, EndDate, Selected(Drivers), Selected(Vehicles));
                Rows.Clear();
                foreach (var d in data) Rows.Add(new ProductivityRow(d));
                Totals = ProductivityTotals.Of(data);
                OnPropertyChanged(nameof(Totals));
                OnPropertyChanged(nameof(HasRows));
                HasRun = true;
                OnPropertyChanged(nameof(EmptyText));
                SavePdfCommand.NotifyCanExecuteChanged();
                SaveExcelCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Productivity report: {ex.Message}");
                ErrorText = T("productivity.LoadError");
            }
            finally { IsBusy = false; }
        }

        private bool CanGenerate() => !IsBusy;
        private bool CanSave() => HasRows;

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void SavePdf()
        {
            var path = AskPath("pdf", T("productivity.PdfFilter"));
            if (path is null) return;
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            new ProductivityDocument(Rows.Select(r => r.Data).ToList(), StartDate, EndDate).GeneratePdf(path);
            Open(path);
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void SaveExcel()
        {
            var path = AskPath("xlsx", T("productivity.ExcelFilter"));
            if (path is null) return;
            ProductivityWorkbook.Save(Rows.Select(r => r.Data).ToList(), StartDate, EndDate, path);
            Open(path);
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

        private static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { FileLogger.Log($"Productivity report: could not open {path}. {ex.Message}"); }
        }

        private static List<int> Selected(IEnumerable<ReportFilterOption> options) => options.Where(o => o.IsSelected).Select(o => o.Id).ToList();
    }

    /// <summary>One driver's day, ready to show: the figures and their wording.</summary>
    public class ProductivityRow
    {
        public ProductivityRow(ProductivityReportRowDto data) => Data = data;

        public ProductivityReportRowDto Data { get; }

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
        public string Reliability => Issues.Count == 0 ? "OK" : Issues.Count == 1 ? "!" : "!!";
        public bool IsReliable => Issues.Count == 0;
        public string ReliabilityTip => Issues.Count == 0 ? T("productivity.ReliableTip") : string.Join("\n", Issues);

        private List<string> Issues
        {
            get
            {
                var list = new List<string>();
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
                var parts = new (string key, int minutes, string color)[]
                {
                    ("productivity.HLoaded", Data.LoadedMinutes, "#673AB7"),
                    ("productivity.HInspection", Data.InspectionMinutes, "#0EA5E9"),
                    ("productivity.XScheduleWait", Data.ScheduleWaitMinutes, "#F59E0B"),
                    ("productivity.XPickupWait", Data.PickupWaitMinutes, "#FB7185"),
                    ("productivity.HDeadhead", Data.DeadheadDriveMinutes, "#64748B"),
                    ("productivity.HOther", Data.OtherMinutes, "#CBD5E1"),
                };
                var total = Math.Max(1, parts.Sum(x => x.minutes));
                return parts.Select(x => new DaySegment(T(x.key), ProductivityFormat.Hours(x.minutes),
                    $"{(double)x.minutes / total:P0}", Brush(x.color), BarWidth * x.minutes / total)).ToList();
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
                    new DaySegment(T("productivity.MLoaded"), ProductivityFormat.Miles(Data.LoadedMiles), $"{loaded / total:P0}", Brush("#673AB7"), BarWidth * loaded / total),
                    new DaySegment(T("productivity.MDeadhead"), ProductivityFormat.Miles(Data.DeadheadMiles), $"{empty / total:P0}", Brush("#CBD5E1"), BarWidth * empty / total),
                };
            }
        }

        /// <summary>The smaller mile figures, as chips under the bar.</summary>
        public IReadOnlyList<DetailItem> MileFacts => new[]
        {
            new DetailItem(T("productivity.XGps"), GpsMiles),
            new DetailItem(T("productivity.XPlanned"), PlannedMiles),
            new DetailItem(T("productivity.XPassenger"), PassengerMiles),
            new DetailItem(T("productivity.XOffRoute"), OffRouteMiles),
        };

        public string OdometerRange => Data.OdometerOut is { } o && Data.OdometerIn is { } i ? $"{o:N0} → {i:N0}" : T("productivity.NoOdometer");
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

    public record DetailItem(string Label, string Value);

    /// <summary>One segment of a breakdown bar and its legend line.</summary>
    public record DaySegment(string Label, string Value, string Share, Brush Color, double Width);

    public static class ProductivityFormat
    {
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

        public string PaidText => ProductivityFormat.Hours(PaidMinutes);
        public string MilesText => $"{VehicleMiles:N1} mi";
        public string LoadedText => VehicleMiles > 0 ? $"{LoadedMiles / VehicleMiles:P0}" : "—";

        private static string T(string key) => LocalizationService.Instance[key];
        public string DaysSub => string.Format(T("productivity.SubDays"), Drivers);
        public string PaidSub => Days > 0 ? string.Format(T("productivity.SubPerDay"), ProductivityFormat.Hours(PaidMinutes / Days)) : string.Empty;
        public string MilesSub => Days > 0 ? string.Format(T("productivity.SubPerDay"), $"{VehicleMiles / Days:N1} mi") : string.Empty;
        public string LoadedSub => string.Format(T("productivity.SubLoaded"), $"{LoadedMiles:N0}", $"{PassengerMiles:N0}");
        public string TripsSub => Days > 0 ? string.Format(T("productivity.SubPerDay"), $"{(double)Trips / Days:N1}") : string.Empty;

        public static ProductivityTotals Of(IReadOnlyCollection<ProductivityReportRowDto> rows) => new()
        {
            Days = rows.Count,
            Drivers = rows.Select(r => r.DriverId).Distinct().Count(),
            PaidMinutes = rows.Sum(r => r.PaidMinutes),
            VehicleMiles = rows.Sum(r => r.VehicleMiles ?? 0),
            LoadedMiles = rows.Sum(r => r.LoadedMiles ?? 0),
            DeadheadMiles = rows.Sum(r => r.DeadheadMiles ?? 0),
            PassengerMiles = rows.Sum(r => r.PassengerMiles ?? 0),
            Trips = rows.Sum(r => r.TripsCompleted)
        };
    }
}
