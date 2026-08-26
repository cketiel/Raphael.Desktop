using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services.Notifications;

/// <summary>
/// Keeps the dispatch office inbox: loads it, keeps it live, and owns the read state.
/// </summary>
public sealed class NotificationService : INotificationService
{
    /// <summary>
    /// How often expired notices are swept out of an open panel.
    /// </summary>
    /// <remarks>
    /// The server stops serving a notice twelve hours after it was created, but a panel
    /// left open all shift never asks again. Without this, a dispatcher coming back in the
    /// morning would be looking at yesterday's list and believing it was today's.
    /// </remarks>
    private static readonly TimeSpan ExpirySweepInterval = TimeSpan.FromMinutes(1);

    private readonly NotificationApiClient _apiClient;

    private readonly NotificationSignalRService _signalR;

    private readonly INotificationReadStateStore _readState;

    private readonly List<NotificationDto> _notifications = [];

    private DispatcherTimer? _expiryTimer;

    public IReadOnlyList<NotificationDto> Notifications => _notifications;

    // Archiving does not hide anything, so it does not change these counters either.
    public int UnreadCount =>
        _notifications.Count(n => !_readState.IsRead(n.Id));

    public int PendingActionCount =>
        _notifications.Count(RequiresAction);

    public HubConnectionState ConnectionState => _signalR.State;

    public DateTime? LastReceivedAtUtc { get; private set; }

    public event EventHandler<NotificationDto>? NotificationReceived;

    public event EventHandler? NotificationsChanged;

    public event EventHandler? ReadStateChanged;

    public event EventHandler? ConnectionStateChanged;

    public NotificationService(
        NotificationApiClient apiClient,
        NotificationSignalRService signalR,
        INotificationReadStateStore readState)
    {
        _apiClient = apiClient;
        _signalR = signalR;
        _readState = readState;

        _signalR.NotificationReceived += OnNotificationReceived;

        _signalR.NotificationsRefreshRequested += async (_, _) =>
        {
            await RefreshAsync();
        };

        _signalR.NotificationViewed += (_, recipientId) =>
            UpdateRecipient(recipientId, r => r.ViewedAtUtc ??= DateTime.UtcNow);

        _signalR.NotificationAcknowledged += (_, recipientId) =>
            UpdateRecipient(recipientId, r =>
            {
                r.AcknowledgedAtUtc ??= DateTime.UtcNow;
                r.ViewedAtUtc ??= DateTime.UtcNow;
            });

        _signalR.ConnectionError += (_, _) => RaiseConnectionStateChanged();

        _signalR.ConnectionStateChanged += (_, _) => RaiseConnectionStateChanged();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);

        await _signalR.StartAsync(cancellationToken);

        RaiseConnectionStateChanged();

        StartExpirySweep();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NotificationDto> loaded;

        try
        {
            loaded = await _apiClient.GetNotificationsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Notifications must never stop the application from working. A failed reload
            // leaves the previous list in place; the next refresh picks it up.
            System.Diagnostics.Debug.WriteLine(
                $"Could not load notifications: {ex.Message}");

            return;
        }

        var visible = loaded
            .Where(IsForSomeoneElse)
            .Where(n => !IsExpired(n))
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToList();

        Invoke(() =>
        {
            _notifications.Clear();
            _notifications.AddRange(visible);

            // Anything no longer in the inbox can never be shown again, so its read mark
            // is dead weight in the file.
            _readState.Prune(_notifications.Select(n => n.Id));

            RaiseNotificationsChanged();
        });
    }

    public bool IsRead(Guid notificationId) =>
        _readState.IsRead(notificationId);

    public void SetRead(Guid notificationId, bool isRead)
    {
        _readState.SetRead(notificationId, isRead);
        RaiseReadStateChanged();
    }

    public void SetAllRead(bool isRead)
    {
        // Deliberately does NOT acknowledge anything. Acknowledging a Will Call sends the
        // patient a message promising a vehicle; a bulk action must never be able to make
        // that promise on a dispatcher's behalf.
        _readState.SetRead(_notifications.Select(n => n.Id).ToList(), isRead);

        RaiseReadStateChanged();
    }

    public async Task SetArchivedAsync(
        Guid notificationId,
        bool isArchived,
        CancellationToken cancellationToken = default)
    {
        await _apiClient.SetArchivedAsync(
            notificationId,
            isArchived,
            cancellationToken);

        Invoke(() =>
        {
            var notification = _notifications
                .FirstOrDefault(n => n.Id == notificationId);

            if (notification is null)
                return;

            // Mirrors what the server just did, so the row updates without a round trip.
            // The status the server computes when un-archiving depends on whether the
            // reading window has closed; Delivered is the harmless guess, and the next
            // refresh brings back the real one.
            notification.Status = isArchived
                ? NotificationKeys.Status.Archived
                : "Delivered";

            RaiseNotificationsChanged();
        });
    }

    public async Task AcknowledgeAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = _notifications
            .FirstOrDefault(n => n.Id == notificationId);

        var recipient = notification?.Recipients.FirstOrDefault();

        if (recipient is null || recipient.Id == Guid.Empty)
            return;

        if (recipient.IsAcknowledged)
            return;

        await _apiClient.MarkAcknowledgedAsync(recipient.Id, cancellationToken);

        Invoke(() =>
        {
            recipient.AcknowledgedAtUtc = DateTime.UtcNow;
            recipient.ViewedAtUtc ??= DateTime.UtcNow;

            // Whoever takes charge has plainly read it.
            _readState.SetRead(notificationId, true);

            RaiseNotificationsChanged();
        });
    }

    public async Task StopAsync()
    {
        _expiryTimer?.Stop();
        _expiryTimer = null;

        await _signalR.StopAsync();
    }

    private void OnNotificationReceived(object? sender, NotificationDto notification)
    {
        if (notification is null)
            return;

        LastReceivedAtUtc = DateTime.UtcNow;

        // Nobody is told about what they just did themselves. An office notice is stored
        // once for the whole office, so the server cannot leave the author out of the
        // broadcast the way it does with an individual recipient: the client has to.
        if (!IsForSomeoneElse(notification))
            return;

        Invoke(() =>
        {
            var existing = _notifications
                .FirstOrDefault(n => n.Id == notification.Id);

            if (existing is not null)
                _notifications.Remove(existing);

            _notifications.Insert(0, notification);

            RaiseNotificationsChanged();
            RaiseConnectionStateChanged();

            NotificationReceived?.Invoke(this, notification);
        });
    }

    /// <summary>
    /// False when this dispatcher is the one who caused the notification.
    /// </summary>
    private static bool IsForSomeoneElse(NotificationDto notification)
    {
        if (!notification.Metadata.TryGetValue(
                NotificationKeys.Metadata.PerformedByUserId,
                out var performedBy) ||
            string.IsNullOrWhiteSpace(performedBy) ||
            string.IsNullOrWhiteSpace(SessionManager.UserId))
        {
            return true;
        }

        return !string.Equals(
            performedBy.Trim(),
            SessionManager.UserId.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Still waiting for somebody in the office to take charge. Same rule the panel's
    /// pending tab uses, so the badge and the tab cannot disagree.
    /// </summary>
    private static bool RequiresAction(NotificationDto notification)
    {
        var recipient = notification.Recipients.FirstOrDefault();

        return NotificationKeys.RequiresAction(
            notification.Type,
            notification.BusinessEventCode,
            recipient?.IsAcknowledged ?? false);
    }

    private static bool IsExpired(NotificationDto notification) =>
        notification.ExpiresAtUtc.HasValue &&
        notification.ExpiresAtUtc.Value <= DateTime.UtcNow;

    private void UpdateRecipient(Guid recipientId, Action<NotificationRecipientDto> apply)
    {
        Invoke(() =>
        {
            var recipient = _notifications
                .SelectMany(n => n.Recipients)
                .FirstOrDefault(r => r.Id == recipientId);

            if (recipient is null)
                return;

            apply(recipient);

            RaiseNotificationsChanged();
        });
    }

    private void StartExpirySweep()
    {
        if (_expiryTimer is not null)
            return;

        if (Application.Current is null)
            return;

        _expiryTimer = new DispatcherTimer
        {
            Interval = ExpirySweepInterval
        };

        _expiryTimer.Tick += (_, _) =>
        {
            var expired = _notifications.Where(IsExpired).ToList();

            if (expired.Count == 0)
                return;

            foreach (var notification in expired)
                _notifications.Remove(notification);

            _readState.Prune(_notifications.Select(n => n.Id));

            RaiseNotificationsChanged();
        };

        _expiryTimer.Start();
    }

    private void RaiseNotificationsChanged() =>
        NotificationsChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseReadStateChanged() =>
        ReadStateChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseConnectionStateChanged() =>
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);

    private static void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
