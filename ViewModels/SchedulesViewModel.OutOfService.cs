using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// Routes out of service in the Schedule tab (RE-053): not offered for routing, a banner on the
    /// one on screen, and the trips an Out of Service sent back picked out in the unscheduled list.
    /// </summary>
    /// <remarks>
    /// A route is out of service while its vehicle or its driver is. The open records are read once
    /// when the tab loads and again only when the board says they changed: never per route, never
    /// on a timer.
    /// </remarks>
    public partial class SchedulesViewModel
    {
        private readonly IOutOfServiceService _outOfService = new OutOfServiceService();

        private List<OutOfServiceRecordDto> _outOfServiceRecords = new();

        /// <summary>The trips to pick out in the unscheduled list, for the life of the tab: they are ids, so another day simply has none of them.</summary>
        private HashSet<int> _outOfServiceHighlight = new();

        private async Task LoadOutOfServiceAsync()
        {
            try
            {
                _outOfServiceRecords = await _outOfService.GetActiveAsync();
            }
            catch (Exception ex)
            {
                // The server refuses routing on such a route anyway; a list that could not be read
                // only means the combo offers it and the refusal explains why.
                Helpers.FileLogger.Log($"Schedule: could not read the routes out of service. {ex.Message}");
                _outOfServiceRecords = new();
            }

            RaiseOutOfService();
        }

        /// <summary>The open records of a route: its vehicle's and its driver's.</summary>
        private List<OutOfServiceRecordDto> OutOfServiceOf(VehicleRoute route) =>
            route is null
                ? new()
                : _outOfServiceRecords.Where(r =>
                        (r.Subject == Views.OutOfServiceDialog.Vehicle && r.SubjectId == route.VehicleId)
                        || (r.Subject == Views.OutOfServiceDialog.Driver && r.SubjectId == route.DriverId))
                    .ToList();

        /// <summary>Neither out of service nor suspended on the selected day.</summary>
        private bool IsRoutable(VehicleRoute route)
        {
            if (OutOfServiceOf(route).Count > 0) return false;

            var day = SelectedDate.Date;
            return route.Suspensions == null
                   || !route.Suspensions.Any(s => s.SuspensionStart.Date <= day && s.SuspensionEnd.Date >= day);
        }

        public bool IsSelectedRouteOutOfService => SelectedVehicleRoute != null && OutOfServiceOf(SelectedVehicleRoute).Count > 0;

        public string SelectedRouteOutOfServiceText
        {
            get
            {
                if (!IsSelectedRouteOutOfService) return null;

                var L = LocalizationService.Instance;
                var why = string.Join("; ", OutOfServiceOf(SelectedVehicleRoute).Select(r =>
                    string.Format(L[r.Subject == Views.OutOfServiceDialog.Vehicle ? "oos.BannerVehicle" : "oos.BannerDriver"],
                        r.SubjectName, L["oos.reason." + r.ReasonCode])));

                return string.Format(L["oos.Banner"], SelectedVehicleRoute.Name, why);
            }
        }

        public string OutOfServiceRowTooltip => LocalizationService.Instance["oos.RowTooltip"];

        private void RaiseOutOfService()
        {
            OnPropertyChanged(nameof(IsSelectedRouteOutOfService));
            OnPropertyChanged(nameof(SelectedRouteOutOfServiceText));
            RouteTripCommand?.NotifyCanExecuteChanged();
        }

        private void ApplyOutOfServiceHighlight()
        {
            if (_outOfServiceHighlight.Count == 0) return;

            foreach (var trip in UnscheduledTrips)
                if (_outOfServiceHighlight.Contains(trip.Id) && !trip.IsOutOfServiceHighlighted)
                    trip.IsOutOfServiceHighlighted = true;
        }

        /// <summary>
        /// Opens a day's unscheduled list with these trips picked out: where the Out of Service
        /// result's link lands.
        /// </summary>
        public async Task ShowUnscheduledAsync(DateTime date, IReadOnlyCollection<int> tripIds)
        {
            _outOfServiceHighlight = tripIds.ToHashSet();

            if (!IsInitialized)
            {
                // The tab is still opening: its own first load reads this day, and the highlight
                // is applied as the list fills.
                _selectedDate = date.Date;
                OnPropertyChanged(nameof(SelectedDate));
                return;
            }

            SelectedDate = date.Date;
            ApplyOutOfServiceHighlight();
            await Task.CompletedTask;
        }

        /// <summary>
        /// Routes went out of service or came back. One message for the whole operation: the
        /// unscheduled list is reloaded once, and only when its day is one of those that got trips.
        /// </summary>
        private void OnBoardRouteServiceChanged(object sender, RouteServiceChangedMessage message)
        {
            if (message is null) return;

            OnUiThread(async () =>
            {
                await LoadOutOfServiceAsync();
                ApplyVehicleRouteFilter();

                if (message.UnroutedDates.Any(d => d.Date == SelectedDate.Date))
                {
                    foreach (var id in message.UnroutedTripIds) _outOfServiceHighlight.Add(id);

                    // ⚠️ The server sends RouteChanged for the route on screen just before this, and
                    // that reload holds _isDataLoading, which makes ReloadUnscheduledTripsAsync skip.
                    // Without the wait the backlog stayed without the trips that had just come back.
                    for (var waited = 0; _isDataLoading && waited < 10_000; waited += 100)
                        await Task.Delay(100);

                    await ReloadUnscheduledTripsAsync();
                }
            });
        }
    }
}
