using Microsoft.AspNetCore.SignalR.Client;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class NotificationSignalRService : IAsyncDisposable
    {
        private readonly HubConnection _connection;

        private bool _started;

        public event EventHandler<NotificationDto>? NotificationReceived;

        public event EventHandler<Guid>? NotificationViewed;

        public event EventHandler<Guid>? NotificationAcknowledged;

        public event EventHandler<Guid>? NotificationUnviewed;

        /// <summary>The connection came back after a drop: messages sent meanwhile were lost.</summary>
        public event EventHandler? Reconnected;

        public event EventHandler<IReadOnlyList<Guid>>? NotificationsViewed;

        public event EventHandler<Exception>? ConnectionError;

        /// <summary>
        /// Raised whenever the hub connects, drops or reconnects.
        /// </summary>
        /// <remarks>
        /// Connection errors are not shown to the user — SignalR reconnects on its own and
        /// a modal about it would be noise. But a dispatcher staring at a panel that stopped
        /// receiving has no way to tell it apart from a quiet morning, so the Notification
        /// Center needs to know in order to show its channel health banner.
        /// </remarks>
        public event EventHandler? ConnectionStateChanged;

        public HubConnectionState State =>
            _connection.State;

        public NotificationSignalRService()
        {
            var baseUrl = ApiEnvironment.BaseUrl.TrimEnd('/');

            var hubUrl =
                $"{baseUrl}/hubs/notifications"; // app.MapHub<NotificationHub>("/hubs/notifications"); 

            _connection =
                new HubConnectionBuilder()
                    .WithUrl(
                        hubUrl,
                        options =>
                        {
                            options.AccessTokenProvider =
                                () =>
                                {
                                    return Task.FromResult(
                                        SessionManager.Token);
                                };
                        })
                    .WithAutomaticReconnect(new LiveReconnectPolicy())
                    .Build();

            RegisterHandlers();

            _connection.Reconnecting +=
                OnReconnectingAsync;

            _connection.Reconnected +=
                OnReconnectedAsync;

            _connection.Closed +=
                OnClosedAsync;
        }

        private void RegisterHandlers()
        {
            _connection.On<NotificationDto>(
                "ReceiveNotification",
                notification =>
                {
                    NotificationReceived?.Invoke(
                        this,
                        notification);
                });

            _connection.On<Guid>(
                "NotificationViewed",
                notificationRecipientId =>
                {
                    NotificationViewed?.Invoke(
                        this,
                        notificationRecipientId);
                });

            _connection.On<Guid>(
                "NotificationAcknowledged",
                notificationRecipientId =>
                {
                    NotificationAcknowledged?.Invoke(
                        this,
                        notificationRecipientId);
                });

            _connection.On<Guid>(
                "NotificationUnviewed",
                notificationRecipientId =>
                {
                    NotificationUnviewed?.Invoke(
                        this,
                        notificationRecipientId);
                });

            _connection.On<List<Guid>>(
                "NotificationsViewed",
                notificationRecipientIds =>
                {
                    NotificationsViewed?.Invoke(
                        this,
                        notificationRecipientIds);
                });

            // "RefreshNotifications" is deliberately not handled (RE-078). It goes to the whole
            // office group, so every open Desktop reloaded the full inbox at the same second.
            // Every change now arrives as its own incremental message, and a reconnection
            // reloads on its own.
            _connection.On("RefreshNotifications", () => { });
        }

        public async Task StartAsync(
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(
                    SessionManager.Token))
            {
                return;
            }

            _wanted = true;

            if (_connection.State ==
                HubConnectionState.Connected)
            {
                _started = true;
                return;
            }

            if (_connection.State ==
                HubConnectionState.Connecting ||
                _connection.State ==
                HubConnectionState.Reconnecting)
            {
                return;
            }

            try
            {
                await _connection.StartAsync(
                    cancellationToken);

                _started = true;
            }
            catch (Exception ex)
            {
                _started = false;

                ConnectionError?.Invoke(
                    this,
                    ex);

                _ = KeepConnectingAsync();
            }
            finally
            {
                ConnectionStateChanged?.Invoke(
                    this,
                    EventArgs.Empty);
            }
        }

        private volatile bool _wanted;
        private int _connecting;

        /// <summary>RE-090: the automatic reconnection does not cover a first start that failed, nor a closed one.</summary>
        private async Task KeepConnectingAsync()
        {
            if (Interlocked.Exchange(ref _connecting, 1) == 1) return;
            try
            {
                for (long attempt = 1; _wanted && _connection.State == HubConnectionState.Disconnected; attempt++)
                {
                    await Task.Delay(LiveReconnectPolicy.Delay(attempt));
                    if (!_wanted) return;
                    try
                    {
                        await _connection.StartAsync();
                        _started = true;
                        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
                        // Whatever arrived meanwhile is read again, as after any reconnection.
                        Reconnected?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception)
                    {
                        // The next attempt says. Telling it each time would fill the log on a bad day.
                    }
                }
            }
            finally { Interlocked.Exchange(ref _connecting, 0); }
        }

        public async Task StopAsync()
        {
            _wanted = false;

            if (_connection.State ==
                HubConnectionState.Disconnected)
            {
                _started = false;
                return;
            }

            try
            {
                await _connection.StopAsync();
            }
            finally
            {
                _started = false;
            }
        }

        private Task OnReconnectingAsync(
            Exception? exception)
        {
            if (exception != null)
            {
                ConnectionError?.Invoke(
                    this,
                    exception);
            }

            ConnectionStateChanged?.Invoke(
                this,
                EventArgs.Empty);

            return Task.CompletedTask;
        }

        private Task OnReconnectedAsync(
            string? connectionId)
        {
            _started = true;

            ConnectionStateChanged?.Invoke(
                this,
                EventArgs.Empty);

            Reconnected?.Invoke(
                this,
                EventArgs.Empty);

            return Task.CompletedTask;
        }

        private Task OnClosedAsync(
            Exception? exception)
        {
            _started = false;

            if (exception != null)
            {
                ConnectionError?.Invoke(
                    this,
                    exception);
            }

            ConnectionStateChanged?.Invoke(
                this,
                EventArgs.Empty);

            if (_wanted) _ = KeepConnectingAsync();

            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
    }
}