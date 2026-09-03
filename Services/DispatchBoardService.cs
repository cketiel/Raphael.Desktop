using Microsoft.AspNetCore.SignalR.Client;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// The dispatch board channel: what the other dispatchers and the drivers are doing, live.
    /// </summary>
    /// <remarks>
    /// A second connection alongside the notification one, on purpose. The bell is a person's
    /// inbox — its messages are stored, counted and kept for twelve hours. These are not
    /// messages for a person at all: they exist to keep a screen truthful while it is open, and
    /// nothing about them survives being missed. Mixing the two would mean a vehicle's position
    /// every thirty seconds arriving through the same pipe as a cancellation somebody has to
    /// read.
    ///
    /// The screen says what it is looking at — the day, and the route on screen — so a
    /// dispatcher working Tuesday is not woken by Wednesday's traffic.
    /// </remarks>
    public class DispatchBoardService : IDispatchBoardService
    {
        private readonly HubConnection _connection;

        private string? _watchedDay;
        private int? _watchedRouteId;
        private string? _watchedRouteDay;

        public event EventHandler<TripRoutedMessage>? TripRouted;
        public event EventHandler<TripUnroutedMessage>? TripUnrouted;
        public event EventHandler<RouteChangedMessage>? RouteChanged;
        public event EventHandler<VehiclePositionMessage>? VehiclePosition;

        public HubConnectionState State => _connection.State;

        public DispatchBoardService()
        {
            var baseUrl = App.Configuration["ApiAddress:ApiTest"];

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("ApiAddress:ApiTest is not configured.");
            }

            var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/dispatch";

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(SessionManager.Token);
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHandlers();

            // A reconnection starts with empty group membership on the server, so whatever the
            // screen was watching has to be asked for again. Without this the tab comes back
            // connected and silent, which looks exactly like a quiet morning.
            _connection.Reconnected += async _ => await RestoreWatchesAsync();
        }

        public async Task StartAsync()
        {
            if (_connection.State != HubConnectionState.Disconnected) return;

            try
            {
                await _connection.StartAsync();
                await RestoreWatchesAsync();
            }
            catch (Exception ex)
            {
                // Never fatal. The screen works without the live channel; it just stops being
                // told what other people do, which is how it behaved before this existed.
                System.Diagnostics.Debug.WriteLine($"[board] could not connect: {ex.Message}");
            }
        }

        public async Task WatchDayAsync(DateTime date)
        {
            var day = Day(date);
            if (_watchedDay == day) return;

            var previous = _watchedDay;
            _watchedDay = day;

            if (_connection.State != HubConnectionState.Connected) return;

            if (previous != null) await SafeInvokeAsync("UnwatchBoard", previous);
            await SafeInvokeAsync("WatchBoard", day);
        }

        public async Task WatchRouteAsync(int vehicleRouteId, DateTime date)
        {
            var day = Day(date);
            if (_watchedRouteId == vehicleRouteId && _watchedRouteDay == day) return;

            var previousId = _watchedRouteId;
            var previousDay = _watchedRouteDay;

            _watchedRouteId = vehicleRouteId;
            _watchedRouteDay = day;

            if (_connection.State != HubConnectionState.Connected) return;

            if (previousId.HasValue && previousDay != null)
                await SafeInvokeAsync("UnwatchRoute", previousId.Value, previousDay);

            await SafeInvokeAsync("WatchRoute", vehicleRouteId, day);
        }

        public async Task StopAsync()
        {
            _watchedDay = null;
            _watchedRouteId = null;
            _watchedRouteDay = null;

            try
            {
                await _connection.StopAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[board] could not stop cleanly: {ex.Message}");
            }
        }

        private void RegisterHandlers()
        {
            _connection.On<TripRoutedMessage>(
                "TripRouted", m => TripRouted?.Invoke(this, m));

            _connection.On<TripUnroutedMessage>(
                "TripUnrouted", m => TripUnrouted?.Invoke(this, m));

            _connection.On<RouteChangedMessage>(
                "RouteChanged", m => RouteChanged?.Invoke(this, m));

            _connection.On<VehiclePositionMessage>(
                "VehiclePosition", m => VehiclePosition?.Invoke(this, m));
        }

        private async Task RestoreWatchesAsync()
        {
            if (_connection.State != HubConnectionState.Connected) return;

            if (_watchedDay != null)
                await SafeInvokeAsync("WatchBoard", _watchedDay);

            if (_watchedRouteId.HasValue && _watchedRouteDay != null)
                await SafeInvokeAsync("WatchRoute", _watchedRouteId.Value, _watchedRouteDay);
        }

        private async Task SafeInvokeAsync(string method, params object?[] args)
        {
            try
            {
                await _connection.InvokeCoreAsync(method, args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[board] {method} failed: {ex.Message}");
            }
        }

        /// <summary>
        /// The day as the server names its groups. Invariant: a group name is an identifier, and
        /// one that changed shape with the machine's culture would put two dispatchers in the
        /// same office on channels that never meet.
        /// </summary>
        private static string Day(DateTime date) =>
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
