using Microsoft.AspNetCore.SignalR.Client;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Raphael.Desktop.Services
{
    /// <summary>Null message: the channel came back and something may have been missed.</summary>
    public sealed class TripsChangedEventArgs : EventArgs
    {
        public TripsChangedEventArgs(TripsChangedMessage? message) => Message = message;
        public TripsChangedMessage? Message { get; }
    }

    /// <summary>
    /// RE-090: every trip created, edited, routed, cancelled or deleted, by whoever and from wherever.
    /// One connection for the whole application, shared by Home and every Dispatch tab.
    /// </summary>
    /// <remarks>Screens subscribe weakly, as with <see cref="FleetBoard"/>: a closed tab must not be kept alive.</remarks>
    public sealed class TripBoard
    {
        public static TripBoard Instance { get; } = new();

        /// <summary>A blink is not worth saying.</summary>
        private static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(6);

        private readonly object _gate = new();
        private DispatchBoardService? _board;
        private DispatcherTimer? _lostTimer;

        private TripBoard() { }

        /// <summary>Raised on the UI thread.</summary>
        public event EventHandler<TripsChangedEventArgs>? Changed;

        /// <summary>Raised on the UI thread when <see cref="IsLost"/> changes.</summary>
        public event EventHandler? LostChanged;

        /// <summary>The screens are not being told anything: what they show may be behind.</summary>
        public bool IsLost { get; private set; }

        public void EnsureStarted()
        {
            DispatchBoardService board;
            lock (_gate)
            {
                if (_board is not null) return;
                _board = board = new DispatchBoardService();
            }

            board.TripsChanged += (_, m) => OnUi(() => Changed?.Invoke(this, new TripsChangedEventArgs(m)));
            board.Reconnected += (_, _) => OnUi(() => Changed?.Invoke(this, new TripsChangedEventArgs(null)));
            board.StateChanged += (_, _) => OnUi(() => Judge(board));

            _ = StartAsync(board);
        }

        private static async Task StartAsync(DispatchBoardService board)
        {
            try
            {
                // Queued first, so the start itself asks for the group once it is connected.
                await board.WatchTripsAsync();
                await board.StartAsync();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Trip board: could not start. {ex.Message}");
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

        private void Judge(DispatchBoardService board)
        {
            if (!ReferenceEquals(board, _board)) return;

            if (board.State == HubConnectionState.Connected)
            {
                _lostTimer?.Stop();
                SetLost(false);
                return;
            }

            if (_lostTimer is null)
            {
                _lostTimer = new DispatcherTimer { Interval = LostAfter };
                _lostTimer.Tick += (_, _) =>
                {
                    _lostTimer.Stop();
                    if (_board is { } now) SetLost(now.State != HubConnectionState.Connected);
                };
            }
            if (!IsLost && !_lostTimer.IsEnabled) _lostTimer.Start();
        }

        private void SetLost(bool lost)
        {
            if (IsLost == lost) return;
            IsLost = lost;
            FileLogger.Log(lost ? "Live channel lost." : "Live channel back.");
            LostChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void OnUi(Action action) => Application.Current?.Dispatcher?.BeginInvoke(action);
    }
}
