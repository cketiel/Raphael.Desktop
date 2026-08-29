using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Raphael.Desktop.ViewModels.Admin
{
    /// <summary>
    /// The Google Maps control panel: what we are spending, what the cache is saving, and the
    /// two settings that move both numbers.
    /// </summary>
    /// <remarks>
    /// Built to be shown to somebody who will check it. Every figure here is an estimate of a
    /// Google invoice, and the panel says so where the reader can see it rather than in a footnote
    /// — three things make it an estimate: Google's volume bands are monthly, so a period that is
    /// not a calendar month prices a volume Google never saw; the bands are per Cloud project, so
    /// another application sharing the key eats the same free allowance; and three of the six
    /// products are counted from what the map pages report rather than measured on the server.
    /// </remarks>
    public class GoogleMapsAdminViewModel : BaseViewModel
    {
        private readonly IMapsUsageApiService _service;

        /// <summary>Google's blue, for what we bought. Green for what the cache answered free.</summary>
        private static readonly SKColor BilledColour = new SKColor(0xE5, 0x39, 0x35);

        private static readonly SKColor CachedColour = new SKColor(0x2E, 0x7D, 0x32);

        public GoogleMapsAdminViewModel() : this(new MapsUsageApiService()) { }

        public GoogleMapsAdminViewModel(IMapsUsageApiService service)
        {
            _service = service;

            // Opens on the current calendar month, which is the period Google actually bills.
            var today = DateTime.Today;

            _from = new DateTime(today.Year, today.Month, 1);
            _to = today;

            RefreshCommand = new AsyncRelayCommand(_ => LoadAsync());
            SaveSettingsCommand = new AsyncRelayCommand(_ => SaveSettingsAsync());
            ExportCsvCommand = new RelayCommandObject(_ => ExportCsv());

            ThisMonthCommand = new RelayCommandObject(_ => SetPeriod(PeriodPreset.ThisMonth));
            LastMonthCommand = new RelayCommandObject(_ => SetPeriod(PeriodPreset.LastMonth));
            Last30DaysCommand = new RelayCommandObject(_ => SetPeriod(PeriodPreset.Last30Days));
            Last90DaysCommand = new RelayCommandObject(_ => SetPeriod(PeriodPreset.Last90Days));
        }

        // ---------------------------------------------------------------- period

        private DateTime _from;
        public DateTime From
        {
            get => _from;
            set => SetProperty(ref _from, value);
        }

        private DateTime _to;
        public DateTime To
        {
            get => _to;
            set => SetProperty(ref _to, value);
        }

        private enum PeriodPreset { ThisMonth, LastMonth, Last30Days, Last90Days }

        private void SetPeriod(PeriodPreset preset)
        {
            var today = DateTime.Today;

            switch (preset)
            {
                case PeriodPreset.ThisMonth:
                    From = new DateTime(today.Year, today.Month, 1);
                    To = today;
                    break;

                case PeriodPreset.LastMonth:
                    var first = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                    From = first;
                    To = first.AddMonths(1).AddDays(-1);
                    break;

                case PeriodPreset.Last30Days:
                    From = today.AddDays(-29);
                    To = today;
                    break;

                case PeriodPreset.Last90Days:
                    From = today.AddDays(-89);
                    To = today;
                    break;
            }

            _ = LoadAsync();
        }

        /// <summary>
        /// Warns when the chosen period is not a calendar month, because the cost figures then
        /// price a volume Google's monthly bands never saw.
        /// </summary>
        public bool IsSimulatedPeriod =>
            From.Day != 1 || To != new DateTime(From.Year, From.Month, 1).AddMonths(1).AddDays(-1);

        // ---------------------------------------------------------------- state

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set { SetProperty(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatus)); }
        }

        public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

        // ---------------------------------------------------------------- figures

        private MapsUsageSummaryDto _summary = new MapsUsageSummaryDto();
        public MapsUsageSummaryDto Summary
        {
            get => _summary;
            set
            {
                SetProperty(ref _summary, value);
                OnPropertyChanged(nameof(CacheHitPercentText));
                OnPropertyChanged(nameof(EstimatedCostText));
                OnPropertyChanged(nameof(AvoidedCostText));
                OnPropertyChanged(nameof(ProjectedCostText));
                OnPropertyChanged(nameof(BilledText));
                OnPropertyChanged(nameof(CachedText));
                OnPropertyChanged(nameof(SavingsRatioText));
            }
        }

        private MapsUsageTotalsDto _totals = new MapsUsageTotalsDto();
        public MapsUsageTotalsDto Totals
        {
            get => _totals;
            set
            {
                SetProperty(ref _totals, value);
                OnPropertyChanged(nameof(TotalsText));
            }
        }

        public ObservableCollection<MapsSkuUsageDto> BySku { get; } =
            new ObservableCollection<MapsSkuUsageDto>();

        public ObservableCollection<MapsPricingTierDto> PricingTiers { get; } =
            new ObservableCollection<MapsPricingTierDto>();

        public ObservableCollection<DailyRow> DailyRows { get; } =
            new ObservableCollection<DailyRow>();

        /// <summary>One day as the detail table shows it, both outcomes on one line.</summary>
        public class DailyRow
        {
            public DateTime Day { get; set; }

            public long Billed { get; set; }

            public long Cached { get; set; }

            public long Total => Billed + Cached;

            public double CacheHitPercent => Total == 0 ? 0 : (double)Cached / Total * 100;
        }

        public string BilledText => Summary.TotalBilled.ToString("N0");

        public string CachedText => Summary.TotalCached.ToString("N0");

        public string CacheHitPercentText => (Summary.CacheHitRate * 100).ToString("0.0") + " %";

        public string EstimatedCostText => Summary.EstimatedCost.ToString("C2", Usd);

        public string AvoidedCostText => Summary.AvoidedCost.ToString("C2", Usd);

        public string ProjectedCostText => Summary.ProjectedMonthCost.HasValue
            ? Summary.ProjectedMonthCost.Value.ToString("C2", Usd)
            : "—";

        /// <summary>
        /// The sentence the whole panel exists to produce: of everything this period would have
        /// cost, what share the cache absorbed.
        /// </summary>
        public string SavingsRatioText
        {
            get
            {
                var wouldHaveCost = Summary.EstimatedCost + Summary.AvoidedCost;

                if (wouldHaveCost <= 0) return "—";

                return (Summary.AvoidedCost / wouldHaveCost * 100).ToString("0.0") + " %";
            }
        }

        public string TotalsText
        {
            get
            {
                if (Totals.FirstDay is null) return "Sin datos todavía.";

                return $"{Totals.Billed:N0} a Google · {Totals.Cached:N0} desde caché · "
                     + $"{Totals.CacheHitRate * 100:0.0} % de acierto  "
                     + $"({Totals.FirstDay:d} – {Totals.LastDay:d})";
            }
        }

        private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

        // ---------------------------------------------------------------- charts

        private ISeries[] _dailySeries = Array.Empty<ISeries>();
        public ISeries[] DailySeries
        {
            get => _dailySeries;
            set => SetProperty(ref _dailySeries, value);
        }

        private Axis[] _dailyXAxes = Array.Empty<Axis>();
        public Axis[] DailyXAxes
        {
            get => _dailyXAxes;
            set => SetProperty(ref _dailyXAxes, value);
        }

        private ISeries[] _hitRateSeries = Array.Empty<ISeries>();
        public ISeries[] HitRateSeries
        {
            get => _hitRateSeries;
            set => SetProperty(ref _hitRateSeries, value);
        }

        private ISeries[] _skuSeries = Array.Empty<ISeries>();
        public ISeries[] SkuSeries
        {
            get => _skuSeries;
            set => SetProperty(ref _skuSeries, value);
        }

        public Axis[] CountAxes { get; } = new[]
        {
            new Axis { Name = "Peticiones", MinLimit = 0 }
        };

        public Axis[] PercentAxes { get; } = new[]
        {
            new Axis { Name = "% servido desde caché", MinLimit = 0, MaxLimit = 100 }
        };

        // ---------------------------------------------------------------- view toggles

        private bool _showDailyAsTable;

        /// <summary>
        /// Per card, not per panel: an administrator reading one chart usually wants the numbers
        /// of a different one at the same time.
        /// </summary>
        public bool ShowDailyAsTable
        {
            get => _showDailyAsTable;
            set { SetProperty(ref _showDailyAsTable, value); OnPropertyChanged(nameof(ShowDailyAsChart)); }
        }

        public bool ShowDailyAsChart => !ShowDailyAsTable;

        private bool _showSkuAsTable = true;
        public bool ShowSkuAsTable
        {
            get => _showSkuAsTable;
            set { SetProperty(ref _showSkuAsTable, value); OnPropertyChanged(nameof(ShowSkuAsChart)); }
        }

        public bool ShowSkuAsChart => !ShowSkuAsTable;

        // ---------------------------------------------------------------- settings

        private string _trafficMode = "MaxSavings";
        public string TrafficMode
        {
            get => _trafficMode;
            set { SetProperty(ref _trafficMode, value); OnPropertyChanged(nameof(IsPrecision)); }
        }

        /// <summary>
        /// Drives the warning beside the switch. Precision doubles the price of every routing
        /// call and halves the free allowance, and that should not be a quiet change.
        /// </summary>
        public bool IsPrecision =>
            string.Equals(TrafficMode, "Precision", StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<string> TrafficModes { get; } = new[] { "MaxSavings", "Precision" };

        private string _cacheRetentionDays = "365";
        public string CacheRetentionDays
        {
            get => _cacheRetentionDays;
            set => SetProperty(ref _cacheRetentionDays, value);
        }

        private string _bufferPercent = "12";
        public string BufferPercent
        {
            get => _bufferPercent;
            set => SetProperty(ref _bufferPercent, value);
        }

        // ---------------------------------------------------------------- commands

        public ICommand RefreshCommand { get; }

        public ICommand SaveSettingsCommand { get; }

        public ICommand ExportCsvCommand { get; }

        public ICommand ThisMonthCommand { get; }

        public ICommand LastMonthCommand { get; }

        public ICommand Last30DaysCommand { get; }

        public ICommand Last90DaysCommand { get; }

        // ---------------------------------------------------------------- loading

        public async Task LoadAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                var summary = await _service.GetSummaryAsync(From, To);
                var daily = await _service.GetDailyAsync(From, To);
                var totals = await _service.GetTotalsAsync();
                var pricing = await _service.GetPricingAsync();
                var settings = await _service.GetSettingsAsync();

                Summary = summary;
                Totals = totals;

                BySku.Clear();
                foreach (var sku in summary.BySku.OrderByDescending(s => s.EstimatedCost))
                {
                    BySku.Add(sku);
                }

                PricingTiers.Clear();
                foreach (var tier in pricing) PricingTiers.Add(tier);

                ApplySettings(settings);
                BuildCharts(daily);

                OnPropertyChanged(nameof(IsSimulatedPeriod));
            }
            catch (Exception ex)
            {
                ErrorMessage = "No se pudieron leer los datos de consumo: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplySettings(List<SystemSettingDto> settings)
        {
            string Read(string key, string fallback) =>
                settings.FirstOrDefault(s => s.Key == key)?.Value ?? fallback;

            TrafficMode = Read("Routing.TrafficMode", "MaxSavings");
            CacheRetentionDays = Read("Routing.CacheRetentionDays", "365");
            BufferPercent = Read("Routing.DefaultBufferPercent", "12");
        }

        /// <summary>
        /// Turns the daily points into the three charts.
        /// </summary>
        /// <remarks>
        /// The stacked columns are the one that tells the story: a red band that shrinks while a
        /// green one grows is the whole optimisation in a single picture.
        /// </remarks>
        private void BuildCharts(List<MapsUsagePointDto> points)
        {
            var byDay = points
                .GroupBy(p => p.Day)
                .Select(g => new DailyRow
                {
                    Day = g.Key,
                    Billed = g.Sum(x => x.Billed),
                    Cached = g.Sum(x => x.Cached)
                })
                .OrderBy(r => r.Day)
                .ToList();

            DailyRows.Clear();
            foreach (var row in byDay) DailyRows.Add(row);

            DailySeries = new ISeries[]
            {
                new StackedColumnSeries<long>
                {
                    Name = "Comprado a Google",
                    Values = byDay.Select(r => r.Billed).ToArray(),
                    Fill = new SolidColorPaint(BilledColour),
                    Stroke = null
                },
                new StackedColumnSeries<long>
                {
                    Name = "Servido desde caché",
                    Values = byDay.Select(r => r.Cached).ToArray(),
                    Fill = new SolidColorPaint(CachedColour),
                    Stroke = null
                }
            };

            DailyXAxes = new[]
            {
                new Axis
                {
                    Labels = byDay.Select(r => r.Day.ToString("dd/MM")).ToArray(),

                    // A ninety-day period would otherwise print ninety overlapping labels.
                    LabelsRotation = byDay.Count > 20 ? 45 : 0,
                    TextSize = 11
                }
            };

            HitRateSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "% servido desde caché",
                    Values = byDay.Select(r => r.CacheHitPercent).ToArray(),
                    Stroke = new SolidColorPaint(CachedColour) { StrokeThickness = 3 },
                    Fill = null,
                    GeometrySize = byDay.Count > 40 ? 0 : 6,
                    GeometryStroke = new SolidColorPaint(CachedColour) { StrokeThickness = 2 }
                }
            };

            SkuSeries = BySku
                .Where(s => s.Billed > 0)
                .Select(s => (ISeries)new PieSeries<long>
                {
                    Name = s.DisplayName,
                    Values = new long[] { s.Billed }
                })
                .ToArray();
        }

        private async Task SaveSettingsAsync()
        {
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                var errors = new List<string>();

                async Task Save(string key, string value)
                {
                    var error = await _service.SetSettingAsync(key, value);

                    if (!string.IsNullOrWhiteSpace(error)) errors.Add(error);
                }

                await Save("Routing.TrafficMode", TrafficMode);
                await Save("Routing.CacheRetentionDays", CacheRetentionDays);
                await Save("Routing.DefaultBufferPercent", BufferPercent);

                if (errors.Count > 0)
                {
                    ErrorMessage = string.Join("  ", errors);
                }
                else
                {
                    // The server caches settings for a minute, so the change is live everywhere
                    // within that — worth saying, or an administrator will click twice.
                    StatusMessage = "Guardado. Activo en todo el sistema en menos de un minuto.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "No se pudo guardar la configuración: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Writes the period to a CSV beside the user's documents. A consumption report ends up
        /// in an email sooner or later, and retyping a grid into one is how figures get wrong.
        /// </summary>
        private void ExportCsv()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"raphael-maps-{From:yyyyMMdd}-{To:yyyyMMdd}.csv");

                var csv = new StringBuilder();

                csv.AppendLine("Producto,Comprado a Google,Servido desde cache,% cache,"
                             + "Coste estimado USD,Coste evitado USD,Gratis al mes,Gratis restante");

                foreach (var sku in BySku)
                {
                    csv.AppendLine(string.Join(",",
                        Quote(sku.DisplayName),
                        sku.Billed,
                        sku.Cached,
                        sku.CacheHitPercent.ToString("0.0", Usd),
                        sku.EstimatedCost.ToString("0.00", Usd),
                        sku.AvoidedCost.ToString("0.00", Usd),
                        sku.FreeCapPerMonth,
                        sku.FreeRemainingThisMonth));
                }

                csv.AppendLine();
                csv.AppendLine("Dia,Comprado a Google,Servido desde cache,% cache");

                foreach (var row in DailyRows)
                {
                    csv.AppendLine(string.Join(",",
                        row.Day.ToString("yyyy-MM-dd"),
                        row.Billed,
                        row.Cached,
                        row.CacheHitPercent.ToString("0.0", Usd)));
                }

                File.WriteAllText(path, csv.ToString(), Encoding.UTF8);

                StatusMessage = "Exportado a " + path;
            }
            catch (Exception ex)
            {
                ErrorMessage = "No se pudo exportar: " + ex.Message;
            }
        }

        private static string Quote(string value) =>
            value != null && value.Contains(",") ? "\"" + value + "\"" : value;
    }
}
