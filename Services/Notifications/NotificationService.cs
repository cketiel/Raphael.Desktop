using Raphael.Desktop.Models;
using System.Windows;

namespace Raphael.Desktop.Services.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly NotificationApiClient _apiClient;
    private readonly NotificationSignalRService _signalR;

    private readonly List<NotificationDto> _notifications = [];

    public IReadOnlyList<NotificationDto> Notifications =>
        _notifications;

    public int UnreadCount =>
        _notifications.Count(
            n => n.Recipients.Any(
                r => !r.IsViewed));

    public event EventHandler<NotificationDto>? NotificationReceived;

    public event EventHandler? NotificationsChanged;

    public NotificationService(
        NotificationApiClient apiClient,
        NotificationSignalRService signalR)
    {
        _apiClient = apiClient;
        _signalR = signalR;

        _signalR.NotificationReceived +=
            OnNotificationReceived;

        _signalR.NotificationsRefreshRequested +=
            async (_, _) =>
            {
                await ReloadAsync();
            };

        _signalR.NotificationViewed +=
            (_, recipientId) =>
            {
                UpdateViewedState(recipientId);
            };

        _signalR.NotificationAcknowledged +=
            (_, recipientId) =>
            {
                UpdateAcknowledgedState(recipientId);
            };
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await ReloadAsync(cancellationToken);

        await _signalR.StartAsync(
            cancellationToken);
    }

    private async Task ReloadAsync(
        CancellationToken cancellationToken = default)
    {
        var notifications =
            await _apiClient.GetNotificationsAsync(
                cancellationToken);

        _notifications.Clear();

        _notifications.AddRange(notifications);

        RaiseNotificationsChanged();
    }

    private void OnNotificationReceived(
        object? sender,
        NotificationDto notification)
    {
        Application.Current.Dispatcher.Invoke(
            () =>
            {
                var existing =
                    _notifications.FirstOrDefault(
                        n => n.Id == notification.Id);

                if (existing is not null)
                {
                    _notifications.Remove(existing);
                }

                _notifications.Insert(
                    0,
                    notification);

                RaiseNotificationsChanged();

                NotificationReceived?.Invoke(
                    this,
                    notification);
            });
    }

    public async Task MarkViewedAsync(
        Guid notificationRecipientId,
        CancellationToken cancellationToken = default)
    {
        await _apiClient.MarkViewedAsync(
            notificationRecipientId,
            cancellationToken);

        UpdateViewedState(
            notificationRecipientId);
    }

    public async Task MarkAcknowledgedAsync(
        Guid notificationRecipientId,
        CancellationToken cancellationToken = default)
    {
        await _apiClient.MarkAcknowledgedAsync(
            notificationRecipientId,
            cancellationToken);

        UpdateAcknowledgedState(
            notificationRecipientId);
    }

    private void UpdateViewedState(
        Guid notificationRecipientId)
    {
        Application.Current.Dispatcher.Invoke(
            () =>
            {
                var notification =
                    _notifications.FirstOrDefault(
                        n => n.Recipients.Any(
                            r => r.Id == notificationRecipientId));

                var recipient =
                    notification?.Recipients.FirstOrDefault(
                        r => r.Id == notificationRecipientId);

                if (recipient is null)
                    return;

                recipient.ViewedAtUtc =
                    DateTime.UtcNow;

                RaiseNotificationsChanged();
            });
    }

    private void UpdateAcknowledgedState(
        Guid notificationRecipientId)
    {
        Application.Current.Dispatcher.Invoke(
            () =>
            {
                var notification =
                    _notifications.FirstOrDefault(
                        n => n.Recipients.Any(
                            r => r.Id == notificationRecipientId));

                var recipient =
                    notification?.Recipients.FirstOrDefault(
                        r => r.Id == notificationRecipientId);

                if (recipient is null)
                    return;

                recipient.AcknowledgedAtUtc =
                    DateTime.UtcNow;

                recipient.ViewedAtUtc ??=
                    DateTime.UtcNow;

                RaiseNotificationsChanged();
            });
    }

    private void RaiseNotificationsChanged()
    {
        NotificationsChanged?.Invoke(
            this,
            EventArgs.Empty);
    }
}