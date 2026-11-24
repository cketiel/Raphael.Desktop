using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.LiveMap
{
    /// <summary>
    /// RE-085: the Today panel follows the day, and the list of the trips in progress. Starting or
    /// finishing a trip sends nothing this screen hears, so the figures are read every minute: one
    /// small query, never the whole snapshot. The list only while it is open.
    /// </summary>
    public partial class LiveMapViewModel
    {
        private readonly DispatcherTimer _dayTimer = new() { Interval = TimeSpan.FromSeconds(60) };
        private bool _readingDay;

        public ObservableCollection<LiveMapInProgressRow> InProgress { get; } = new();

        [ObservableProperty] private bool _isInProgressOpen;

        public string InProgressTitle => string.Format(LocalizationService.Instance["livemap.InProgressTitle"], InProgress.Count);
        public string InProgressEmpty => LocalizationService.Instance["livemap.InProgressEmpty"];
        public string InProgressHint => LocalizationService.Instance["livemap.InProgressHint"];

        private void InitDay()
        {
            Day.InProgressRequested += () => { IsInProgressOpen = !IsInProgressOpen; if (IsInProgressOpen) _ = RefreshInProgressAsync(); };
            _dayTimer.Tick += async (_, _) => await RefreshDayAsync();
            _dayTimer.Start();
        }

        [RelayCommand]
        private void CloseInProgress() => IsInProgressOpen = false;

        private async Task RefreshDayAsync()
        {
            if (_readingDay) return;
            _readingDay = true;
            try
            {
                if (await _service.GetDayAsync() is { } day) Day.Apply(day);
                if (IsInProgressOpen) await RefreshInProgressAsync();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: could not refresh the day. {ex.Message}");
            }
            finally
            {
                _readingDay = false;
            }
        }

        private async Task RefreshInProgressAsync()
        {
            try
            {
                var trips = await _service.GetInProgressAsync();
                InProgress.Clear();
                foreach (var t in trips) InProgress.Add(RowOf(t));
                foreach (var n in new[] { nameof(InProgressTitle), nameof(InProgressEmpty), nameof(InProgressHint) }) OnPropertyChanged(n);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: could not read the trips in progress. {ex.Message}");
            }
        }

        private LiveMapInProgressRow RowOf(LiveMapInProgressDto t)
        {
            var l = LocalizationService.Instance;
            var line = t.VehicleRouteId is int id && _byRoute.TryGetValue(id, out var v) ? v : null;
            var onBoard = t.Status == "InProgress";
            var late = line?.LateMinutes;
            var lateText = late is not int m ? string.Empty
                : m > 0 ? string.Format(l["livemap.InProgressLate"], m)
                : l["livemap.InProgressOnTime"];

            return new LiveMapInProgressRow(
                t.TripId,
                string.IsNullOrWhiteSpace(t.TripNumber) ? t.TripId.ToString(CultureInfo.InvariantCulture) : t.TripNumber,
                t.CustomerName ?? "—",
                l["livemap.InProgress" + t.Status],
                onBoard
                    ? string.Format(l["livemap.InProgressAppt"], Clock(t.ToTime))
                    : string.Format(l["livemap.InProgressPickup"], Clock(t.FromTime)),
                string.Join(" · ", new[] { line?.Name, line?.DriverName, t.SpaceType }.Where(x => !string.IsNullOrWhiteSpace(x))),
                lateText,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(LiveMapPalette.PaceFromLateness(late))),
                t.VehicleRouteId);
        }

        private static string Clock(TimeSpan? t) => t is TimeSpan x ? DateTime.Today.Add(x).ToString("h:mm tt", CultureInfo.InvariantCulture) : "—";

        /// <summary>A line picked from a list is put on the map even if its group is switched off.</summary>
        public void ShowGroupOf(int vehicleRouteId)
        {
            if (!_byRoute.TryGetValue(vehicleRouteId, out var v)) return;
            var group = Groups.FirstOrDefault(g => g.Group == v.Group);
            if (group is { IsOn: false }) group.IsOn = true;
        }
    }
}
