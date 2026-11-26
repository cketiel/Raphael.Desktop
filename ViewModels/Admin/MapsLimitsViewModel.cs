#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Raphael.Desktop.ViewModels.Admin
{
    public sealed record MapsLimitPreset(string Choice, string PerTrip, string PerLine, string RealDay, string RealMonth, string MaxMonth, bool IsChosen);
    public sealed record MapsLimitDay(string Day, string Suggestions, string Refused, string Checks);

    /// <summary>
    /// RE-099: the daily limits of Google roads for the suggestions, explained with an example of the
    /// office's own size. The question it answers first: how much can these settings spend in a month.
    /// Moving the example changes nothing until the settings are saved.
    /// </summary>
    public partial class MapsLimitsViewModel : ObservableObject
    {
        /// <summary>The volume of production when this was written (2026-11-25): the starting example.</summary>
        public const int ReferenceTrips = 300, ReferenceLines = 30, ReferenceCancelPercent = 30;

        private readonly IMapsUsageApiService _service;
        private MapsBudgetReportDto _report = new();
        private List<MapsPricingTierDto> _tiers = new();
        private bool _loading;
        private static string T(string key) => LocalizationService.Instance[key];
        private static CultureInfo Culture => CultureInfo.GetCultureInfo(LocalizationService.Instance.CurrentLanguage == "es" ? "es-ES" : "en-US");

        public MapsLimitsViewModel(IMapsUsageApiService service) => _service = service;

        public LocalizationService L => LocalizationService.Instance;

        /// <summary>False with a server that does not have the limits.</summary>
        [ObservableProperty] private bool _isAvailable;

        // What is saved with the settings.
        [ObservableProperty] private int _limit = 700;
        [ObservableProperty] private int _top = 3;
        [ObservableProperty] private int _checks = 200;

        // Only the example's.
        [ObservableProperty] private int _tripsPerDay = ReferenceTrips;
        [ObservableProperty] private int _linesPerDay = ReferenceLines;
        [ObservableProperty] private int _cancelPercent = ReferenceCancelPercent;
        [ObservableProperty] private int _days = 30;
        [ObservableProperty] private bool _precision = true;

        public ObservableCollection<MapsLimitPreset> Presets { get; } = new();
        public ObservableCollection<MapsLimitDay> History { get; } = new();

        partial void OnLimitChanged(int value) { if (value is < 0 or > 20_000) Limit = Math.Clamp(value, 0, 20_000); else Work(); }
        partial void OnTopChanged(int value) { if (value is < 0 or > 10) Top = Math.Clamp(value, 0, 10); else Work(); }
        partial void OnChecksChanged(int value) { if (value is < 0 or > 20_000) Checks = Math.Clamp(value, 0, 20_000); else Work(); }
        partial void OnTripsPerDayChanged(int value) { if (value is < 0 or > 5_000) TripsPerDay = Math.Clamp(value, 0, 5_000); else Work(); }
        partial void OnLinesPerDayChanged(int value) { if (value is < 0 or > 500) LinesPerDay = Math.Clamp(value, 0, 500); else Work(); }
        partial void OnCancelPercentChanged(int value) { if (value is < 0 or > 90) CancelPercent = Math.Clamp(value, 0, 90); else Work(); }
        partial void OnDaysChanged(int value) { if (value is < 1 or > 31) Days = Math.Clamp(value, 1, 31); else Work(); }
        partial void OnPrecisionChanged(bool value) => Work();

        private string Sku => Precision ? MapsUsageContract.Skus.RoutesPro : MapsUsageContract.Skus.RoutesEssentials;

        public async Task LoadAsync(List<MapsPricingTierDto> tiers, bool precision)
        {
            var report = await _service.GetBudgetsAsync();
            IsAvailable = report is not null;
            if (report is null) return;
            _loading = true;
            _report = report;
            _tiers = tiers;
            Limit = report.SuggestionsMaxLegsPerDay;
            Top = report.SuggestionsConfirmTop;
            Checks = report.AppointmentChecksPerDay;
            Precision = precision;
            _loading = false;
            Work();
        }

        public async Task SaveAsync(Func<string, string, Task> save)
        {
            if (!IsAvailable) return;
            await save("Routing.SuggestionsMaxLegsPerDay", Limit.ToString(CultureInfo.InvariantCulture));
            await save("Routing.SuggestionsConfirmTop", Top.ToString(CultureInfo.InvariantCulture));
            await save("Routing.AppointmentChecksPerDay", Checks.ToString(CultureInfo.InvariantCulture));
            (_report.SuggestionsMaxLegsPerDay, _report.SuggestionsConfirmTop, _report.AppointmentChecksPerDay) = (Limit, Top, Checks);
            Work();
        }

        /// <summary>Back to what the server has and to the reference example.</summary>
        [RelayCommand]
        private void Undo()
        {
            _loading = true;
            (Limit, Top, Checks) = (_report.SuggestionsMaxLegsPerDay, _report.SuggestionsConfirmTop, _report.AppointmentChecksPerDay);
            (TripsPerDay, LinesPerDay, CancelPercent) = (ReferenceTrips, ReferenceLines, ReferenceCancelPercent);
            Days = 30;
            _loading = false;
            Work();
        }

        /// <summary>The limit that covers the realistic day of the example, rounded up to 50.</summary>
        [RelayCommand]
        private void CoverRealistic() => Limit = (int)Math.Ceiling(RealDay(Top) / 50.0) * 50;

        // ---- The arithmetic, in the open ----

        /// <summary>A trip's list: the patient's road, the same for every line, and two per line confirmed.</summary>
        public static int RoadsPerTrip(int top) => top <= 0 ? 0 : 1 + 2 * top;
        /// <summary>A line's list: three roads per trip confirmed (to its pickup, the trip itself, on to the next stop).</summary>
        public static int RoadsPerLine(int top) => top <= 0 ? 0 : 3 * top;

        private int MaxDay(int top) => TripsPerDay * RoadsPerTrip(top) + LinesPerDay * RoadsPerLine(top);
        private int RealDay(int top) => (int)Math.Ceiling(MaxDay(top) * (100 - CancelPercent) / 100.0);
        /// <summary>The price of these roads in a month that already has the rest of the system's.</summary>
        private decimal Cost(long roads) => _tiers.Count == 0 ? 0m : MapsCost.Of(_tiers, Sku, roads, _report.RoadsBoughtLast30Days);

        private string Money(decimal v) => "$" + v.ToString(v < 100 ? "N2" : "N0", Culture);
        private string Num(long v) => v.ToString("N0", Culture);
        private decimal UnitPrice => _tiers.Where(t => t.Sku == Sku).OrderBy(t => t.FromRequest).Select(t => t.PricePerThousand / 1000m).FirstOrDefault();
        private long FreePerMonth => _tiers.Where(t => t.Sku == Sku).Select(t => (long)t.FreeCapPerMonth).FirstOrDefault();

        // ---- The answer first: what these settings can spend ----

        private long CapRoads => (long)(Top == 0 ? 0 : Limit) * Days + (long)Checks * Days;
        public string CapMonth => Money(Cost(CapRoads));
        public string CapDetail => string.Format(T("gmaps.limCapDetail"),
            Num(Top == 0 ? 0 : Limit), Num(Checks), Days, Num(CapRoads), Money(Cost((long)(Top == 0 ? 0 : Limit) * Days)), Money(Cost(CapRoads) - Cost((long)(Top == 0 ? 0 : Limit) * Days)));

        // ---- The example ----

        public string ExampleTitle => string.Format(T("gmaps.limExampleTitle"), Num(TripsPerDay), Num(LinesPerDay), Top);
        public string StepTrips => string.Format(T("gmaps.limStepTrips"), Num(TripsPerDay), RoadsPerTrip(Top), Top, Num((long)TripsPerDay * RoadsPerTrip(Top)));
        public string StepLines => string.Format(T("gmaps.limStepLines"), Num(LinesPerDay), RoadsPerLine(Top), Top, Num((long)LinesPerDay * RoadsPerLine(Top)));
        public string StepMax => string.Format(T("gmaps.limStepMax"), Num(MaxDay(Top)), Days, Num((long)MaxDay(Top) * Days), Money(UnitPrice * 1000), Money(Cost((long)MaxDay(Top) * Days)));
        public string StepReal => string.Format(T("gmaps.limStepReal"), CancelPercent, Num(RealDay(Top)), Num((long)RealDay(Top) * Days), Money(Cost((long)RealDay(Top) * Days)));
        public string StepFree => FreePerMonth <= 0 ? "" : _report.RoadsBoughtLast30Days >= FreePerMonth
            ? string.Format(T("gmaps.limFreeUsed"), Num(FreePerMonth), Num(_report.RoadsBoughtLast30Days))
            : string.Format(T("gmaps.limFreeLeft"), Num(FreePerMonth), Num(_report.RoadsBoughtLast30Days));

        public string MaxMonth => Money(Cost((long)MaxDay(Top) * Days));
        public string RealMonth => Money(Cost((long)RealDay(Top) * Days));
        public string RealDayText => string.Format(T("gmaps.limRealDay"), Num(RealDay(Top)));
        public string MaxDayText => string.Format(T("gmaps.limMaxDay"), Num(MaxDay(Top)));

        /// <summary>Whether the saved limit reaches the realistic day, said plainly.</summary>
        public string Verdict
        {
            get
            {
                if (Top == 0 || Limit == 0) return T("gmaps.limVerdictOff");
                var real = RealDay(Top);
                if (Limit >= MaxDay(Top)) return string.Format(T("gmaps.limVerdictMax"), Num(Limit), Num(MaxDay(Top)));
                if (Limit >= real) return string.Format(T("gmaps.limVerdictReal"), Num(Limit), Num(real), Num(MaxDay(Top)));
                return string.Format(T("gmaps.limVerdictShort"), Num(Limit), Num(real), (int)Math.Round(Limit * 100.0 / Math.Max(1, real)), Num((int)Math.Ceiling(real / 50.0) * 50));
            }
        }
        public bool IsShort => Top > 0 && Limit > 0 && Limit < RealDay(Top);
        public double CoveredPercent => Top == 0 || RealDay(Top) == 0 ? 0 : Math.Min(100, Limit * 100.0 / RealDay(Top));

        public string WhatIsARoad => string.Format(T("gmaps.limRoad"), "$" + UnitPrice.ToString("N3", Culture), Money(UnitPrice * 1000));
        public string WhatIsTop => Top == 0 ? T("gmaps.limTopZero") : string.Format(T("gmaps.limTopMeaning"), Top, RoadsPerTrip(Top), RoadsPerLine(Top));
        public string NowMonth => string.Format(T("gmaps.limNowMonth"), Money(Cost(0) + (_tiers.Count == 0 ? 0 : MapsCost.Of(_tiers, Sku, _report.RoadsBoughtLast30Days, 0))), Num(_report.RoadsBoughtLast30Days));
        public string RealVolume => _report.TripsPerDay > 0 ? string.Format(T("gmaps.limRealVolume"), Num(_report.TripsPerDay), _report.WorkingDays) : "";

        public bool HasChange => IsAvailable && (Limit != _report.SuggestionsMaxLegsPerDay || Top != _report.SuggestionsConfirmTop || Checks != _report.AppointmentChecksPerDay);
        public string ChangeText
        {
            get
            {
                if (!HasChange) return T("gmaps.limNoChange");
                var was = Cost((long)(_report.SuggestionsConfirmTop == 0 ? 0 : _report.SuggestionsMaxLegsPerDay) * Days + (long)_report.AppointmentChecksPerDay * Days);
                var now = Cost(CapRoads);
                return string.Format(T("gmaps.limChange"), Money(was), Money(now), (now >= was ? "+" : "−") + Money(Math.Abs(now - was)));
            }
        }

        private void Work()
        {
            if (_loading) return;
            Presets.Clear();
            for (var n = 1; n <= 6; n++)
                Presets.Add(new(n.ToString(), RoadsPerTrip(n).ToString(), RoadsPerLine(n).ToString(), Num(RealDay(n)), Money(Cost((long)RealDay(n) * Days)), Money(Cost((long)MaxDay(n) * Days)), n == Top));
            History.Clear();
            foreach (var d in _report.Days.GroupBy(d => d.Day).OrderByDescending(g => g.Key).Take(7))
            {
                var s = d.FirstOrDefault(x => x.Purpose == 1);
                var k = d.FirstOrDefault(x => x.Purpose == 2);
                History.Add(new(d.Key.ToString("d", Culture), string.Format(T("gmaps.limDayUse"), Num(s?.Used ?? 0), Num(s?.Requests ?? 0)), Num(s?.Refused ?? 0), Num(k?.Used ?? 0)));
            }
            OnPropertyChanged(string.Empty);
        }

        public bool HasHistory => History.Count > 0;
        public void Retranslate() => Work();
    }
}
