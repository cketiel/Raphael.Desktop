using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services.Notifications;

public interface INotificationService
{
    IReadOnlyList<NotificationDto> Notifications { get; }

    int UnreadCount { get; }

    event EventHandler<NotificationDto>? NotificationReceived;

    event EventHandler? NotificationsChanged;

    Task InitializeAsync(
        CancellationToken cancellationToken = default);

    Task MarkViewedAsync(
        Guid notificationRecipientId,
        CancellationToken cancellationToken = default);

    Task MarkAcknowledgedAsync(
        Guid notificationRecipientId,
        CancellationToken cancellationToken = default);
}