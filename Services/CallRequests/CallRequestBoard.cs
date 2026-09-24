using System.Windows;
using System.Windows.Threading;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services.CallRequests
{
    /// <summary>
    /// The drivers' call-back queue, one copy for the whole application.
    /// </summary>
    /// <remarks>
    /// Owned by the main window and started at sign-in, not by the Notification Center, which is
    /// created only when somebody opens it: the header counter and the alerts have to work before
    /// that. The panel and the call card are views over this, so they cannot disagree.
    ///
    /// <para>
    /// ⚠️ The server is the truth. Live messages keep this current, a reconnection reloads it, and
    /// a slow timer reloads it anyway: the list is short, and a message lost to a network blip
    /// would otherwise leave a request looking open on one screen all afternoon.
    /// </para>
    /// </remarks>
    public sealed class CallRequestBoard
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan SafetyReloadInterval = TimeSpan.FromMinutes(3);

        private readonly ICallRequestApiClient _api;

        private readonly IDispatchBoardService _hub;

        private readonly Dictionary<int, CallRequestSummaryDto> _requests = new();

        private readonly DispatcherTimer _tick;

        private readonly DispatcherTimer _safetyReload;

        private bool _started;

        public CallRequestBoard(ICallRequestApiClient api, IDispatchBoardService hub)
        {
            _api = api;
            _hub = hub;

            _tick = new DispatcherTimer { Interval = TickInterval };
            _tick.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);

            _safetyReload = new DispatcherTimer { Interval = SafetyReloadInterval };
            _safetyReload.Tick += async (_, _) => await ReloadAsync();
        }

        public ICallRequestApiClient Api => _api;

        /// <summary>The list changed: a reload, a live message, or a change this dispatcher made.</summary>
        public event EventHandler? Changed;

        /// <summary>A live message, after it was applied. For the alerts.</summary>
        public event EventHandler<CallRequestChangedMessage>? LiveChange;

        /// <summary>Every thirty seconds, so waiting times on screen move.</summary>
        public event EventHandler? Tick;

        public IReadOnlyCollection<CallRequestSummaryDto> All => _requests.Values;

        public int WaitingCount => _requests.Values.Count(r => r.Status == CallRequestStatuses.Waiting);

        public CallRequestSummaryDto? OldestWaiting =>
            _requests.Values
                .Where(r => r.Status == CallRequestStatuses.Waiting)
                .OrderBy(r => r.QueuedAtUtc)
                .FirstOrDefault();

        public static int CurrentUserId =>
            int.TryParse(SessionManager.UserId, out var id) ? id : 0;

        public CallRequestSummaryDto? Get(int id) =>
            _requests.TryGetValue(id, out var request) ? request : null;

        public async Task StartAsync()
        {
            if (_started)
                return;

            _started = true;

            // Not awaited: the queue must not wait on a list of providers to show up.
            _ = BusinessDay.LoadAsync();

            _hub.CallRequestChanged += OnHubMessage;
            _hub.Reconnected += OnHubReconnected;

            await _hub.StartAsync();
            await _hub.WatchCallRequestsAsync();

            await ReloadAsync();

            _tick.Start();
            _safetyReload.Start();
        }

        public async Task StopAsync()
        {
            if (!_started)
                return;

            _started = false;

            _tick.Stop();
            _safetyReload.Stop();

            _hub.CallRequestChanged -= OnHubMessage;
            _hub.Reconnected -= OnHubReconnected;

            await _hub.StopAsync();
        }

        public async Task ReloadAsync()
        {
            try
            {
                var fresh = await _api.GetQueueAsync();

                _requests.Clear();

                foreach (var request in fresh)
                    _requests[request.Id] = request;

                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Keep what is on screen. The next reload, or the next live message, corrects it.
                FileLogger.Log($"Call requests: could not reload the queue. {ex.Message}");
            }
        }

        /// <summary>
        /// Applies what the server answered to this dispatcher's own action, without waiting for
        /// the hub to echo it back.
        /// </summary>
        public void Apply(CallRequestSummaryDto? request)
        {
            if (request is null || !Store(request))
                return;

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>True when the request was newer than the copy held, and replaced it.</summary>
        private bool Store(CallRequestSummaryDto request)
        {
            if (_requests.TryGetValue(request.Id, out var held) && held.Revision > request.Revision)
                return false;

            _requests[request.Id] = request;

            return true;
        }

        private void OnHubMessage(object? sender, CallRequestChangedMessage message)
        {
            if (message?.Request is null)
                return;

            // Arrives on the SignalR thread; everything that reads the board lives on the UI one.
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (!Store(message.Request))
                    return;

                Changed?.Invoke(this, EventArgs.Empty);
                LiveChange?.Invoke(this, message);
            });
        }

        private void OnHubReconnected(object? sender, EventArgs e) =>
            Application.Current?.Dispatcher.InvokeAsync(async () => await ReloadAsync());
    }
}
