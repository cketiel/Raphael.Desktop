using Microsoft.AspNetCore.SignalR.Client;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using System;
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

        public event EventHandler? NotificationsRefreshRequested;

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
            var baseUrl =
                App.Configuration["ApiAddress:ApiTest"];

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException(
                    "ApiAddress:ApiTest is not configured.");
            }

            baseUrl = baseUrl.TrimEnd('/');

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
                    .WithAutomaticReconnect()
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

            _connection.On(
                "RefreshNotifications",
                () =>
                {
                    NotificationsRefreshRequested?.Invoke(
                        this,
                        EventArgs.Empty);
                });
        }

        public async Task StartAsync(
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(
                    SessionManager.Token))
            {
                return;
            }

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
            }
            finally
            {
                ConnectionStateChanged?.Invoke(
                    this,
                    EventArgs.Empty);
            }
        }

        public async Task StopAsync()
        {
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

            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
    }
}