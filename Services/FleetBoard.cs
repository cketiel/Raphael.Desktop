using System;
using System.Threading.Tasks;
using System.Windows;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services
{
    /// <summary>What a fleet screen is told: a vehicle or driver went out of service or came back.</summary>
    public sealed class FleetChangedEventArgs : EventArgs
    {
        public FleetChangedEventArgs(RouteServiceChangedMessage? message) => Message = message;

        /// <summary>Null after a reconnection: something may have been missed, read it all again.</summary>
        public RouteServiceChangedMessage? Message { get; }
    }

    /// <summary>
    /// One live channel for the screens that show the fleet (RE-053): Runs, Vehicles and Users.
    /// </summary>
    /// <remarks>
    /// One connection for the whole application, opened by the first screen that asks and shared by
    /// the rest, rather than one per open tab. It only listens to the fleet group, where the server
    /// says that a vehicle or a driver went out of service or came back — whoever did it, from
    /// whichever screen or computer. Schedule keeps its own connection: it watches a day and a route.
    ///
    /// ⚠️ Screens subscribe through <see cref="System.Windows.WeakEventManager{TEventSource,TEventArgs}"/>.
    /// MainWindow builds a new view each time a tab is opened, and a strong handler on this
    /// singleton would keep every closed tab alive, and reloading, for the rest of the session.
    /// </remarks>
    public sealed class FleetBoard
    {
        public static FleetBoard Instance { get; } = new();

        private readonly object _gate = new();
        private DispatchBoardService? _board;

        private FleetBoard() { }

        /// <summary>Raised on the UI thread.</summary>
        public event EventHandler<FleetChangedEventArgs>? Changed;

        /// <summary>Opens the channel if it is not open yet. Never fatal: without it, screens reload as before.</summary>
        public void EnsureStarted()
        {
            DispatchBoardService board;
            lock (_gate)
            {
                if (_board is not null) return;
                _board = board = new DispatchBoardService();
            }

            board.RouteServiceChanged += (_, m) => Raise(m);
            board.Reconnected += (_, _) => Raise(null);

            _ = StartAsync(board);
        }

        private static async Task StartAsync(DispatchBoardService board)
        {
            try
            {
                // Queued first, so the start itself asks for the group once it is connected.
                await board.WatchFleetAsync();
                await board.StartAsync();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Fleet board: could not start. {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            DispatchBoardService? board;
            lock (_gate)
            {
                board = _board;
                _board = null;
            }

            if (board is not null) await board.StopAsync();
        }

        private void Raise(RouteServiceChangedMessage? message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            dispatcher.BeginInvoke(new Action(() => Changed?.Invoke(this, new FleetChangedEventArgs(message))));
        }
    }
}
