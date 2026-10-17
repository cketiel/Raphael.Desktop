using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Views
{
    /// <summary>How a long operation ended: its result, a stop, or a failure.</summary>
    public sealed class BusyOutcome<T>
    {
        public bool Completed { get; init; }
        public bool Stopped { get; init; }
        public T Result { get; init; }
        public Exception Error { get; init; }
    }

    /// <summary>
    /// Runs a long operation behind a modal window that says what is happening, cannot be closed
    /// halfway, and optionally offers a stop (RE-052).
    /// </summary>
    /// <remarks>
    /// ⚠️ Closing is refused while the work runs, and that is the point: a window that disappears
    /// while the server is still saving leaves the user not knowing whether anything happened. A stop
    /// asks the work to cancel and still waits for it to end, because only the work knows whether it
    /// stopped in time.
    /// </remarks>
    public partial class BusyDialog : Window
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _finished;

        private static LocalizationService L => LocalizationService.Instance;

        private BusyDialog(string title, string message, string warning, bool canStop)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
            ShowWarning(warning);
            StopButton.Content = L["busy.Stop"];
            StopButton.Visibility = canStop ? Visibility.Visible : Visibility.Collapsed;
            Closing += OnClosing;
        }

        public static async Task<BusyOutcome<T>> RunAsync<T>(Window owner, string title, string message, string warning,
            bool canStop, Func<CancellationToken, Task<T>> work)
        {
            var dialog = new BusyDialog(title, message, warning, canStop) { Owner = owner };
            Task<T> task = null;

            dialog.Loaded += async (_, _) =>
            {
                task = work(dialog._cts.Token);
                try { await task; } catch { /* read below, once the window is gone */ }
                dialog._finished = true;
                dialog.Close();
            };

            dialog.ShowDialog();

            try
            {
                var result = await task;
                return new BusyOutcome<T> { Completed = true, Result = result };
            }
            catch (OperationCanceledException) when (dialog._cts.IsCancellationRequested)
            {
                return new BusyOutcome<T> { Stopped = true };
            }
            catch (Exception ex)
            {
                return new BusyOutcome<T> { Error = ex };
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            StopButton.IsEnabled = false;
            StopButton.Content = L["busy.Stopping"];
            MessageText.Text = L["busy.StoppingMessage"];
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_finished) return;
            e.Cancel = true;
            ShowWarning(L["busy.DoNotClose"]);
        }

        private void ShowWarning(string warning)
        {
            WarningText.Text = warning;
            WarningBox.Visibility = string.IsNullOrWhiteSpace(warning) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
