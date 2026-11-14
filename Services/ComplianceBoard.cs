using System;
using System.Threading.Tasks;
using System.Windows;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services
{
    /// <summary>What the Compliance screen is told: a document changed, or the channel came back.</summary>
    public sealed class ComplianceChangedEventArgs : EventArgs
    {
        public ComplianceChangedEventArgs(ComplianceChangedMessage? message) => Message = message;

        /// <summary>Null after a reconnection: something may have been missed, read it all again.</summary>
        public ComplianceChangedMessage? Message { get; }
    }

    /// <summary>
    /// The live channel of the Compliance screen (RE-069): a document uploaded from the Driver or the
    /// office, corrected, approved, rejected or expired, by whoever and from wherever.
    /// </summary>
    /// <remarks>
    /// One connection for the whole application, opened by the first Compliance tab and shared by the
    /// rest, like <see cref="FleetBoard"/>. Screens subscribe through
    /// <see cref="System.Windows.WeakEventManager{TEventSource,TEventArgs}"/>, so a closed tab is not
    /// kept alive by this singleton.
    /// </remarks>
    public sealed class ComplianceBoard
    {
        public static ComplianceBoard Instance { get; } = new();

        private readonly object _gate = new();
        private DispatchBoardService? _board;

        private ComplianceBoard() { }

        /// <summary>Raised on the UI thread.</summary>
        public event EventHandler<ComplianceChangedEventArgs>? Changed;

        /// <summary>Opens the channel if it is not open yet. Never fatal: without it the screen still works.</summary>
        public void EnsureStarted()
        {
            DispatchBoardService board;
            lock (_gate)
            {
                if (_board is not null) return;
                _board = board = new DispatchBoardService();
            }

            board.ComplianceChanged += (_, m) => Raise(m);
            board.Reconnected += (_, _) => Raise(null);

            _ = StartAsync(board);
        }

        private static async Task StartAsync(DispatchBoardService board)
        {
            try
            {
                // Queued first, so the start itself asks for the group once it is connected.
                await board.WatchComplianceAsync();
                await board.StartAsync();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Compliance board: could not start. {ex.Message}");
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

        private void Raise(ComplianceChangedMessage? message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            dispatcher.BeginInvoke(new Action(() => Changed?.Invoke(this, new ComplianceChangedEventArgs(message))));
        }
    }
}
