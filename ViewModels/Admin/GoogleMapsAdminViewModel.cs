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
            set
            {
                if (!SetProperty(ref _from, value)) return;

                // A hand-picked date is no longer any of the quick filters, and the buttons
                // should stop claiming otherwise.
                if (!_settingPreset) Preset = PeriodPreset.Custom;

                OnPropertyChanged(nameof(PeriodLabel));
            }
        }

        private DateTime _to;
        public DateTime To
        {
            get => _to;
            set
            {
                if (!SetProperty(ref _to, value)) return;

                if (!_settingPreset) Preset = PeriodPreset.Custom;

                OnPropertyChanged(nameof(PeriodLabel));
            }
        }

        private bool _settingPreset;

        public enum PeriodPreset { ThisMonth, LastMonth, Last30Days, Last90Days, Custom }

        private PeriodPreset _preset = PeriodPreset.ThisMonth;

        /// <summary>
        /// Which quick filter is in force. Bound to the buttons so one of them stays visibly
        /// pressed: an administrator reading figures has to know which period produced them, and
        /// four buttons that all look alike after a click answer nothing.
        /// </summary>
        public PeriodPreset Preset
        {
            get => _preset;
            set
            {
                SetProperty(ref _preset, value);

                OnPropertyChanged(nameof(IsThisMonth));
                OnPropertyChanged(nameof(IsLastMonth));
                OnPropertyChanged(nameof(IsLast30Days));
                OnPropertyChanged(nameof(IsLast90Days));
                OnPropertyChanged(nameof(PeriodLabel));
            }
        }

        public bool IsThisMonth => Preset == PeriodPreset.ThisMonth;

        public bool IsLastMonth => Preset == PeriodPreset.LastMonth;

        public bool IsLast30Days => Preset == PeriodPreset.Last30Days;

        public bool IsLast90Days => Preset == PeriodPreset.Last90Days;

        /// <summary>
        /// The period in words, appended to every title and chart so a screenshot of one card
        /// still says what it is showing.
        /// </summary>
        public string PeriodLabel
        {
            get
            {
                var name = Preset switch
                {
                    PeriodPreset.ThisMonth => L["gmaps.ThisMonth"],
                    PeriodPreset.LastMonth => L["gmaps.LastMonth"],
                    PeriodPreset.Last30Days => L["gmaps.Last30"],
                    PeriodPreset.Last90Days => L["gmaps.Last90"],
                    _ => L["gmaps.Custom"]
                };

                return $"{name}  ·  {From:d} – {To:d}";
            }
        }

        private void SetPeriod(PeriodPreset preset)
        {
            var today = DateTime.Today;

            // Guards the two date setters, which would otherwise flip the choice to Custom the
            // moment this method assigns them.
            _settingPreset = true;

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

            _settingPreset = false;

            Preset = preset;

            _ = LoadAsync();
        }

        /// <summary>Every string on this screen, in whichever language is selected.</summary>
        public LocalizationService L => LocalizationService.Instance;

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

        public ObservableCollection<PricingRow> PricingRows { get; } =
            new ObservableCollection<PricingRow>();

        /// <summary>
        /// Google's price list the way Google prints it: one row per product, one column per
        /// volume band.
        /// </summary>
        /// <remarks>
        /// The database stores one row per band, which is the right shape to calculate with and
        /// the wrong one to read — thirty rows saying "Geocoding" six times over. Pivoted here so
        /// an administrator can compare two products by running a finger across a line.
        /// </remarks>
        public class PricingRow
        {
            public string DisplayName { get; set; } = string.Empty;

            /// <summary>Free requests a month, as a figure with thousands separators.</summary>
            public string FreeTier { get; set; } = string.Empty;

            public string Band10k { get; set; } = "—";

            public string Band100k { get; set; } = "—";

            public string Band500k { get; set; } = "—";

            public string Band1M { get; set; } = "—";

            public string Band5M { get; set; } = "—";

            /// <summary>
            /// The most expensive product on the list, shown in bold — it is the one whose volume
            /// an administrator most needs to notice.
            /// </summary>
            public bool IsHighlighted { get; set; }
        }

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
                if (Totals.FirstDay is null) return L["gmaps.NoDataYet"];

                return $"{Totals.Billed:N0} → {L["gmaps.ToGoogle"]}  ·  {Totals.Cached:N0} → {L["gmaps.FromCache"]}  ·  "
                     + $"{Totals.CacheHitRate * 100:0.0} % {L["gmaps.CacheHit"]}  "
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
            new Axis { MinLimit = 0 }
        };

        // Axis names are left off on purpose: the series legend already says what is plotted, in
        // the selected language, and an axis title would need rebuilding on every switch.
        public Axis[] PercentAxes { get; } = new[]
        {
            new Axis { MinLimit = 0, MaxLimit = 100 }
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

                BuildPricingRows(pricing);

                ApplySettings(settings);
                BuildCharts(daily);

                OnPropertyChanged(nameof(IsSimulatedPeriod));
            }
            catch (Exception ex)
            {
                ErrorMessage = L["gmaps.LoadFailed"] + ex.Message;
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
        /// Pivots the price bands into one row per product, the way Google's own page prints it.
        /// </summary>
        /// <remarks>
        /// The bands are matched by the request they start at rather than by position, so a
        /// product Google prices differently — Routes Pro, whose free tier ends at 5,000 and
        /// whose charging therefore starts there — still lands in the right column.
        /// </remarks>
        private void BuildPricingRows(List<MapsPricingTierDto> tiers)
        {
            PricingRows.Clear();

            foreach (var group in tiers.GroupBy(t => t.Sku))
            {
                var bands = group.OrderBy(t => t.FromRequest).ToList();
                var first = bands[0];

                string Price(int upTo)
                {
                    var band = bands.FirstOrDefault(b => (b.ToRequest ?? int.MaxValue) == upTo);

                    return band == null ? "—" : band.PricePerThousand.ToString("C2", Usd);
                }

                string Top()
                {
                    var band = bands.FirstOrDefault(b => b.ToRequest == null);

                    return band == null ? "—" : band.PricePerThousand.ToString("C2", Usd);
                }

                PricingRows.Add(new PricingRow
                {
                    DisplayName = first.DisplayName,
                    FreeTier = first.FreeCapPerMonth.ToString("N0"),
                    Band10k = Price(100_000),
                    Band100k = Price(500_000),
                    Band500k = Price(1_000_000),
                    Band1M = Price(5_000_000),
                    Band5M = Top(),

                    // Routes Pro is the one that costs double and gives half the free tier.
                    IsHighlighted = group.Key == "RoutesPro"
                });
            }
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
                    Name = L["gmaps.BoughtFromGoogle"],
                    Values = byDay.Select(r => r.Billed).ToArray(),
                    Fill = new SolidColorPaint(BilledColour),
                    Stroke = null
                },
                new StackedColumnSeries<long>
                {
                    Name = L["gmaps.ServedFromCache"],
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
                    StatusMessage = L["gmaps.Saved"];
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = L["gmaps.SaveFailed"] + ex.Message;
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

                csv.AppendLine("Product,BilledToGoogle,ServedFromCache,CachePercent,"
                             + "EstimatedCostUSD,AvoidedCostUSD,FreePerMonth,FreeRemaining");

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
                csv.AppendLine("Day,BilledToGoogle,ServedFromCache,CachePercent");

                foreach (var row in DailyRows)
                {
                    csv.AppendLine(string.Join(",",
                        row.Day.ToString("yyyy-MM-dd"),
                        row.Billed,
                        row.Cached,
                        row.CacheHitPercent.ToString("0.0", Usd)));
                }

                File.WriteAllText(path, csv.ToString(), Encoding.UTF8);

                StatusMessage = L["gmaps.ExportedTo"] + path;
            }
            catch (Exception ex)
            {
                ErrorMessage = L["gmaps.ExportFailed"] + ex.Message;
            }
        }

        private static string Quote(string value) =>
            value != null && value.Contains(",") ? "\"" + value + "\"" : value;
    }
}
