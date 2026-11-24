using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// RE-090: the trips on screen follow what anybody does to them. A few changed trips are a few rows
    /// read again, never the day: with drivers working, trips change all day long.
    /// </summary>
    public partial class HomeViewModel
    {
        private const int MaxRows = 50;

        private readonly HashSet<int> _staleTrips = new();
        private bool _staleDay;
        private bool _liveReading;
        private DispatcherTimer _liveTimer;

        private void InitializeLive()
        {
            WeakEventManager<TripBoard, TripsChangedEventArgs>.AddHandler(TripBoard.Instance, nameof(TripBoard.Changed), OnTripsChangedElsewhere);
        }

        private bool OnScreen(DateTime date) =>
            date.Date >= FilterDate.Date && date.Date <= (IsDateRange ? FilterEndDate!.Value.Date : FilterDate.Date);

        private void OnTripsChangedElsewhere(object sender, TripsChangedEventArgs e)
        {
            if (e.Message is null) _staleDay = true;   // the channel came back: anything may have changed
            else
            {
                foreach (var change in e.Message.Trips)
                    if (OnScreen(change.Date) || (change.PreviousDate is DateTime was && OnScreen(was)) || TripsByDate.Any(t => t.Id == change.TripId))
                        _staleTrips.Add(change.TripId);
            }
            if (!_staleDay && _staleTrips.Count == 0) return;

            if (_liveTimer is null)
            {
                _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _liveTimer.Tick += async (_, _) => await ReadStaleAsync();
            }
            _liveTimer.Stop();
            _liveTimer.Start();
        }

        private async Task ReadStaleAsync()
        {
            if (_liveReading || IsLoadingTrips) return;   // the timer keeps ticking until it is done

            _liveTimer.Stop();
            _liveReading = true;
            var (start, end) = (FilterDate, IsDateRange ? FilterEndDate : null);
            var whole = _staleDay || _staleTrips.Count > MaxRows;
            var ids = _staleTrips.ToList();
            _staleDay = false;
            _staleTrips.Clear();

            try
            {
                var service = new TripService();
                List<TripReadDto>? rows = whole ? null : await service.GetRowsAsync(ids);
                whole = rows is null;
                if (whole) rows = end is DateTime to ? await service.GetTripsByDateRangeAsync(start, to) : await service.GetTripsByDateAsync(start);

                // Another day was asked for meanwhile, and that one is fresh already.
                if (start != FilterDate || end != (IsDateRange ? FilterEndDate : null) || IsLoadingTrips) return;
                ApplyRows(rows!, whole ? null : ids);
            }
            catch (Exception ex)
            {
                // Nobody asked for it: the next change, or the reconnection, reads it again.
                FileLogger.Log($"Home live update: {ex.Message}");
            }
            finally { _liveReading = false; }

            if (_staleDay || _staleTrips.Count > 0) _liveTimer.Start();
        }

        /// <param name="asked">The trips asked for, or null when <paramref name="rows"/> is everything on screen.</param>
        private void ApplyRows(List<TripReadDto> rows, List<int>? asked)
        {
            var byId = rows.Where(r => OnScreen(r.Date)).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
            var selectedId = SelectedTrip?.Id;
            var changed = false;

            for (var i = TripsByDate.Count - 1; i >= 0; i--)
            {
                var old = TripsByDate[i];
                if (asked is not null && !asked.Contains(old.Id)) continue;

                if (!byId.Remove(old.Id, out var fresh))
                {
                    // The trip being worked on keeps its row until the dispatcher leaves it.
                    if (TripBeingEdited?.Id == old.Id) continue;
                    TripsByDate.RemoveAt(i);
                    changed = true;
                }
                else if (System.Text.Json.JsonSerializer.Serialize(old) != System.Text.Json.JsonSerializer.Serialize(fresh))
                {
                    WithSelectionKept(() => TripsByDate[i] = fresh);
                    changed = true;
                }
            }

            foreach (var fresh in byId.Values.OrderBy(t => t.Date).ThenBy(t => t.FromTime))
            {
                var at = 0;
                while (at < TripsByDate.Count && (TripsByDate[at].Date, TripsByDate[at].FromTime ?? TimeSpan.Zero).CompareTo((fresh.Date, fresh.FromTime ?? TimeSpan.Zero)) <= 0) at++;
                TripsByDate.Insert(at, fresh);
                changed = true;
            }

            if (!changed) return;

            WithSelectionKept(() =>
            {
                Filters.Rebuild(TripsByDate);
                Filters.WatchOptions();
                RefreshTripsView();
                if (selectedId is int id && SelectedTrip?.Id != id && TripsByDate.FirstOrDefault(t => t.Id == id) is { } row) SelectedTrip = row;
            });
        }

        /// <summary>A row put in the place of another is not the dispatcher moving to it.</summary>
        private void WithSelectionKept(Action change)
        {
            var before = _restoringSelection;
            var id = SelectedTrip?.Id;
            _restoringSelection = true;
            try
            {
                change();
                if (id is int i && SelectedTrip?.Id != i && TripsByDate.FirstOrDefault(t => t.Id == i) is { } row) SelectedTrip = row;
            }
            finally { _restoringSelection = before; }
        }
    }
}
