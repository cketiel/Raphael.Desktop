using System;
using System.Threading.Tasks;
using System.Windows;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// The live channel of billing (RE-072/074): a funding source's rates changed, or some trips'
    /// stored charge was rewritten, by whoever and from wherever (another dispatcher, Booking, the
    /// charge worker).
    /// </summary>
    /// <remarks>
    /// One connection for the whole application, opened by the first screen that needs it, like
    /// <see cref="ComplianceBoard"/>. The messages carry ids only; screens re-read what they show
    /// through the usual endpoints. A null message means the channel came back after a drop and
    /// something may have been missed: read it all again.
    /// </remarks>
    public sealed class BillingBoard
    {
        public static BillingBoard Instance { get; } = new();

        private readonly object _gate = new();
        private DispatchBoardService? _board;

        private BillingBoard() { }

        /// <summary>Raised on the UI thread.</summary>
        public event Action<BillingRatesChangedMessage?>? RatesChanged;

        /// <summary>Raised on the UI thread.</summary>
        public event Action<TripChargesChangedMessage?>? TripChargesChanged;

        /// <summary>Opens the channel if it is not open yet. Never fatal: without it the screens still work.</summary>
        public void EnsureStarted()
        {
            DispatchBoardService board;
            lock (_gate)
            {
                if (_board is not null) return;
                _board = board = new DispatchBoardService();
            }

            board.BillingRatesChanged += (_, m) => OnUi(() => RatesChanged?.Invoke(m));
            board.TripChargesChanged += (_, m) => OnUi(() => TripChargesChanged?.Invoke(m));
            board.Reconnected += (_, _) => OnUi(() =>
            {
                RatesChanged?.Invoke(null);
                TripChargesChanged?.Invoke(null);
            });

            _ = StartAsync(board);
        }

        private static async Task StartAsync(DispatchBoardService board)
        {
            try
            {
                // Queued first, so the start itself asks for the group once it is connected.
                await board.WatchBillingAsync();
                await board.StartAsync();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Billing board: could not start. {ex.Message}");
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

        private static void OnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            dispatcher.BeginInvoke(action);
        }
    }
}
