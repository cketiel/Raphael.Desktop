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

        HubConnectionState State { get; }

        Task StartAsync();

        /// <summary>Listen to one day's backlog: trips routed and unrouted by anyone.</summary>
        Task WatchDayAsync(DateTime date);

        /// <summary>Listen to one route: its order, its hours and its vehicle.</summary>
        Task WatchRouteAsync(int vehicleRouteId, DateTime date);

        Task StopAsync();
    }
}
