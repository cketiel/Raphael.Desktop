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
        private bool _watchingCallRequests;

        public event EventHandler<TripRoutedMessage>? TripRouted;
        public event EventHandler<TripUnroutedMessage>? TripUnrouted;
        public event EventHandler<RouteChangedMessage>? RouteChanged;
        public event EventHandler<VehiclePositionMessage>? VehiclePosition;
        public event EventHandler<CallRequestChangedMessage>? CallRequestChanged;
        public event EventHandler<RouteServiceChangedMessage>? RouteServiceChanged;
        public event EventHandler<ComplianceChangedMessage>? ComplianceChanged;
        public event EventHandler<BillingRatesChangedMessage>? BillingRatesChanged;
        public event EventHandler<TripChargesChangedMessage>? TripChargesChanged;
        public event EventHandler<TripsChangedMessage>? TripsChanged;
        public event EventHandler<RouteChangedMessage>? RouteStopsChanged;
        public event EventHandler? StateChanged;
        public event EventHandler? Reconnected;

        public HubConnectionState State => _connection.State;

        public DispatchBoardService()
        {
            var hubUrl = $"{ApiEnvironment.BaseUrl.TrimEnd('/')}/hubs/dispatch";

            Trace($"hub url {hubUrl}");

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(SessionManager.Token);
                })
                .WithAutomaticReconnect(new LiveReconnectPolicy())
                .Build();

            RegisterHandlers();

            // A reconnection starts with empty group membership on the server, so whatever the
            // screen was watching has to be asked for again. Without this the tab comes back
            // connected and silent, which looks exactly like a quiet morning.
            _connection.Reconnected += async _ =>
            {
                Trace("reconnected, asking for the groups again");
                await RestoreWatchesAsync();
                Reconnected?.Invoke(this, EventArgs.Empty);
                StateChanged?.Invoke(this, EventArgs.Empty);
            };

            _connection.Reconnecting += _ =>
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;
            };

            _connection.Closed += ex =>
            {
                Trace($"closed{(ex is null ? string.Empty : $": {ex.Message}")}");
                StateChanged?.Invoke(this, EventArgs.Empty);
                // Closed for good by the server (a deploy, a recycled pool): opened again from here.
                if (_wanted) _ = KeepConnectingAsync(missedSomething: true);
                return Task.CompletedTask;
            };
        }

        private volatile bool _wanted;
        private int _connecting;

        public async Task StartAsync()
        {
            _wanted = true;

            if (_connection.State != HubConnectionState.Disconnected)
            {
                Trace($"start skipped, already {_connection.State}");
                return;
            }

            if (string.IsNullOrWhiteSpace(SessionManager.Token))
            {
                Trace("no session token yet — the hub would refuse the connection");
            }

            // Once here; from then on in the background. Never fatal: the screen works without it.
            if (!await TryConnectAsync()) _ = KeepConnectingAsync(missedSomething: true);
        }

        private async Task<bool> TryConnectAsync()
        {
            try
            {
                await _connection.StartAsync();
                Trace($"connected ({_connection.State})");

                await RestoreWatchesAsync();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Trace($"could not connect: {ex.GetType().Name}: {ex.Message}");
                StateChanged?.Invoke(this, EventArgs.Empty);
                return _connection.State != HubConnectionState.Disconnected;
            }
        }

        private async Task KeepConnectingAsync(bool missedSomething)
        {
            if (System.Threading.Interlocked.Exchange(ref _connecting, 1) == 1) return;
            try
            {
                for (long attempt = 1; _wanted && _connection.State == HubConnectionState.Disconnected; attempt++)
                {
                    await Task.Delay(LiveReconnectPolicy.Delay(attempt));
                    if (!_wanted) return;
                    if (!await TryConnectAsync()) continue;
                    if (missedSomething) Reconnected?.Invoke(this, EventArgs.Empty);
                }
            }
            finally { System.Threading.Interlocked.Exchange(ref _connecting, 0); }
        }

        public async Task WatchDayAsync(DateTime date)
        {
            var day = Day(date);
            if (_watchedDay == day) return;

            var previous = _watchedDay;
            _watchedDay = day;

            if (_connection.State != HubConnectionState.Connected)
            {
                // Remembered, not lost: RestoreWatchesAsync asks for it once the connection is up.
                Trace($"day {day} queued, connection is {_connection.State}");
                return;
            }

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

            if (_connection.State != HubConnectionState.Connected)
            {
                Trace($"route {vehicleRouteId} on {day} queued, connection is {_connection.State}");
                return;
            }

            if (previousId.HasValue && previousDay != null)
                await SafeInvokeAsync("UnwatchRoute", previousId.Value, previousDay);

            await SafeInvokeAsync("WatchRoute", vehicleRouteId, day);
        }

        public async Task WatchCallRequestsAsync()
        {
            if (_watchingCallRequests) return;

            _watchingCallRequests = true;

            if (_connection.State != HubConnectionState.Connected)
            {
                Trace($"call requests queued, connection is {_connection.State}");
                return;
            }

            await SafeInvokeAsync("WatchCallRequests");
        }

        private bool _watchingFleet;

        public async Task WatchFleetAsync()
        {
            if (_watchingFleet) return;

            _watchingFleet = true;

            if (_connection.State != HubConnectionState.Connected)
            {
                Trace($"fleet queued, connection is {_connection.State}");
                return;
            }

            await SafeInvokeAsync("WatchFleet");
        }

        private bool _watchingTrips;

        public async Task WatchTripsAsync()
        {
            if (_watchingTrips) return;

            _watchingTrips = true;

            if (_connection.State != HubConnectionState.Connected)
            {
                Trace($"trips queued, connection is {_connection.State}");
                return;
            }

            await SafeInvokeAsync("WatchTrips");
        }

        private bool _watchingMap;

        /// <summary>RE-080: every vehicle position of the caller's scope, for the live map. Office staff only.</summary>
        public async Task WatchMapAsync()
        {
            if (_watchingMap) return;

            _watchingMap = true;

            if (_connection.State != HubConnectionState.Connected)
            {
                Trace($"map queued, connection is {_connection.State}");
                return;
            }

            await SafeInvokeAsync("WatchMap");
        }

        private bool _watchingCompliance;

        /// <summary>RE-069: compliance documents changing, for the Compliance screen. Office staff only.</summary>
        public async Task WatchComplianceAsync()
        {
            if (_watchingCompliance) return;

            _watchingCompliance = true;

            if (_connection.State != HubConnectionState.Connected)
            {
                Trace($"compliance queued, connection is {_connection.State}");
                return;
            }

            await SafeInvokeAsync("WatchCompliance");
        }

        private bool _watchingBilling;

        /// <summary>RE-072/074: rates and trip charges changing, for the rates screen and Home's Billing. Office staff only.</summary>
        public async Task WatchBillingAsync()
        {
            if (_watchingBilling) return;

            _watchingBilling = true;

            if (_connection.State != HubConnectionState.Connected)
            {
                Trace($"billing queued, connection is {_connection.State}");
                return;
            }

            await SafeInvokeAsync("WatchBilling");
        }

        public async Task StopAsync()
        {
            _wanted = false;
            _watchingTrips = false;
            _watchingBilling = false;
            _watchedDay = null;
            _watchedRouteId = null;
            _watchedRouteDay = null;
            _watchingCallRequests = false;
            _watchingFleet = false;
            _watchingCompliance = false;
            _watchingMap = false;

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
                "TripRouted", m =>
                {
                    Trace($"<- TripRouted trip {m?.TripId} route {m?.VehicleRouteId} on {m?.Date:yyyy-MM-dd}");
                    TripRouted?.Invoke(this, m);
                });

            _connection.On<TripUnroutedMessage>(
                "TripUnrouted", m =>
                {
                    Trace($"<- TripUnrouted trip {m?.TripId} route {m?.VehicleRouteId} on {m?.Date:yyyy-MM-dd}");
                    TripUnrouted?.Invoke(this, m);
                });

            _connection.On<RouteChangedMessage>(
                "RouteChanged", m =>
                {
                    Trace($"<- RouteChanged route {m?.VehicleRouteId} on {m?.Date:yyyy-MM-dd}");
                    RouteChanged?.Invoke(this, m);
                });

            _connection.On<TripsChangedMessage>(
                "TripsChanged", m =>
                {
                    Trace($"<- TripsChanged {m?.Trips?.Count ?? 0} trip(s)");
                    TripsChanged?.Invoke(this, m);
                });

            _connection.On<RouteChangedMessage>(
                "RouteStopsChanged", m =>
                {
                    Trace($"<- RouteStopsChanged route {m?.VehicleRouteId} on {m?.Date:yyyy-MM-dd}");
                    RouteStopsChanged?.Invoke(this, m);
                });

            _connection.On<VehiclePositionMessage>(
                "VehiclePosition", m =>
                {
                    Trace($"<- VehiclePosition route {m?.VehicleRouteId}");
                    VehiclePosition?.Invoke(this, m);
                });

            _connection.On<RouteServiceChangedMessage>(
                "RouteServiceChanged", m =>
                {
                    Trace($"<- RouteServiceChanged out={m?.OutOfService} routes {string.Join(",", m?.VehicleRouteIds ?? new())}");
                    RouteServiceChanged?.Invoke(this, m);
                });

            _connection.On<CallRequestChangedMessage>(
                "CallRequestChanged", m =>
                {
                    Trace($"<- CallRequestChanged {m?.Change} request {m?.Request?.Id}");
                    CallRequestChanged?.Invoke(this, m);
                });

            _connection.On<BillingRatesChangedMessage>(
                "BillingRatesChanged", m =>
                {
                    Trace($"<- BillingRatesChanged funding source {m?.FundingSourceId} space type {m?.SpaceTypeId}");
                    BillingRatesChanged?.Invoke(this, m);
                });

            _connection.On<TripChargesChangedMessage>(
                "TripChargesChanged", m =>
                {
                    Trace($"<- TripChargesChanged {m?.TripIds?.Count ?? 0} trip(s)");
                    TripChargesChanged?.Invoke(this, m);
                });

            _connection.On<ComplianceChangedMessage>(
                "ComplianceChanged", m =>
                {
                    Trace($"<- ComplianceChanged document {m?.DocumentId} {m?.Subject} {m?.SubjectId}");
                    ComplianceChanged?.Invoke(this, m);
                });
        }

        private async Task RestoreWatchesAsync()
        {
            if (_connection.State != HubConnectionState.Connected) return;

            if (_watchedDay != null)
                await SafeInvokeAsync("WatchBoard", _watchedDay);

            if (_watchedRouteId.HasValue && _watchedRouteDay != null)
                await SafeInvokeAsync("WatchRoute", _watchedRouteId.Value, _watchedRouteDay);

            if (_watchingCallRequests)
                await SafeInvokeAsync("WatchCallRequests");

            if (_watchingFleet)
                await SafeInvokeAsync("WatchFleet");

            if (_watchingCompliance)
                await SafeInvokeAsync("WatchCompliance");

            if (_watchingMap)
                await SafeInvokeAsync("WatchMap");

            if (_watchingTrips)
                await SafeInvokeAsync("WatchTrips");

            if (_watchingBilling)
                await SafeInvokeAsync("WatchBilling");
        }

        private async Task SafeInvokeAsync(string method, params object?[] args)
        {
            try
            {
                await _connection.InvokeCoreAsync(method, args);
                Trace($"-> {method}({string.Join(", ", args)})");
            }
            catch (Exception ex)
            {
                Trace($"-> {method} FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// One prefix for the whole channel, so a single run can be read top to bottom in the
        /// output window: what it connected to, what it asked to watch, and what arrived.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void Trace(string message) =>
            System.Diagnostics.Debug.WriteLine($"[board] {message}");

        /// <summary>
        /// The day as the server names its groups. Invariant: a group name is an identifier, and
        /// one that changed shape with the machine's culture would put two dispatchers in the
        /// same office on channels that never meet.
        /// </summary>
        private static string Day(DateTime date) =>
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
