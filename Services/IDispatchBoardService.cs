using Microsoft.AspNetCore.SignalR.Client;
using Raphael.Desktop.DTOs;
using System;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// The live dispatch board: what the other dispatchers and the drivers are doing.
    /// </summary>
    public interface IDispatchBoardService
    {
        event EventHandler<TripRoutedMessage>? TripRouted;

        event EventHandler<TripUnroutedMessage>? TripUnrouted;

        event EventHandler<RouteChangedMessage>? RouteChanged;

        event EventHandler<VehiclePositionMessage>? VehiclePosition;

        event EventHandler<CallRequestChangedMessage>? CallRequestChanged;

        /// <summary>Routes went out of service or came back (RE-053).</summary>
        event EventHandler<RouteServiceChangedMessage>? RouteServiceChanged;

        /// <summary>A compliance document changed (RE-069). Ids only.</summary>
        event EventHandler<ComplianceChangedMessage>? ComplianceChanged;

        /// <summary>
        /// The connection came back. Anything missed while it was down is lost, so a screen that
        /// keeps state reloads it here.
        /// </summary>
        event EventHandler? Reconnected;

        HubConnectionState State { get; }

        Task StartAsync();

        /// <summary>Listen to one day's backlog: trips routed and unrouted by anyone.</summary>
        Task WatchDayAsync(DateTime date);

        /// <summary>Listen to one route: its order, its hours and its vehicle.</summary>
        Task WatchRouteAsync(int vehicleRouteId, DateTime date);

        /// <summary>Listen to the drivers' call-back queue. The server only allows office staff.</summary>
        Task WatchCallRequestsAsync();

        /// <summary>Listen to the fleet's state: routes out of service and back (RE-053). Office staff only.</summary>
        Task WatchFleetAsync();

        /// <summary>RE-080: every vehicle position of the caller's scope, for the live map.</summary>
        Task WatchMapAsync();

        /// <summary>Listen to the compliance documents (RE-069). Office staff only.</summary>
        Task WatchComplianceAsync();

        /// <summary>RE-090: trips created, changed or deleted. Office staff only.</summary>
        event EventHandler<TripsChangedMessage>? TripsChanged;

        /// <summary>RE-090: a stop of the watched route was saved.</summary>
        event EventHandler<RouteChangedMessage>? RouteStopsChanged;

        /// <summary>RE-090: the channel went down or came up.</summary>
        event EventHandler? StateChanged;

        Task WatchTripsAsync();

        Task StopAsync();
    }
}
