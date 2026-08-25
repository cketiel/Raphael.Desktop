using Microsoft.AspNetCore.SignalR.Client;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services.Notifications;

/// <summary>
/// The single copy of the dispatch office inbox inside the application.
/// </summary>
/// <remarks>
/// The bell badge and the Notification Center read the same list from here. Before this,
/// MainWindowViewModel kept its own collection and its own counter, and any second
/// consumer would have started a second, quietly divergent copy.
/// </remarks>
public interface INotificationService
{
    IReadOnlyList<NotificationDto> Notifications { get; }

    /// <summary>Notifications this dispatcher has not opened. Personal, not office-wide.</summary>
    int UnreadCount { get; }

    /// <summary>
    /// Notices that still need somebody to take charge — today, Will Calls nobody has
    /// confirmed. Counted apart from unread on purpose: "I have not looked at it" and
    /// "nobody in the office has taken it" are different facts, and only the second one
    /// has a patient waiting behind it.
    /// </summary>
    int PendingActionCount { get; }

    /// <summary>Live connection state, for the channel health banner.</summary>
    HubConnectionState ConnectionState { get; }

    /// <summary>When the last live notification arrived, or null if none has yet.</summary>
    DateTime? LastReceivedAtUtc { get; }

    event EventHandler<NotificationDto>? NotificationReceived;

    /// <summary>The inbox itself changed: something arrived, expired or was acted on.</summary>
    event EventHandler? NotificationsChanged;

    /// <summary>
    /// Only this dispatcher's read marks changed. Nothing entered or left the inbox.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="NotificationsChanged"/> on purpose. Opening a notification
    /// marks it read, and that happens while the grid is in the middle of committing a
    /// selection change: re-filtering the list right then can drop the very selection that
    /// caused it. Listeners can update a counter from this without touching the view.
    /// </remarks>
    event EventHandler? ReadStateChanged;

    event EventHandler? ConnectionStateChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    bool IsRead(Guid notificationId);

    void SetRead(Guid notificationId, bool isRead);

    /// <summary>Marks every notification currently in the inbox read, or unread.</summary>
    void SetAllRead(bool isRead);

    /// <summary>
    /// Keeps a notification, or lets it go again.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not what a mail client means by archiving. Here it means the cleanup will never
    /// expire or delete this record — for a notice somebody will have to answer for later.
    /// It is a decision about the record, so it goes to the server and holds for everyone.
    ///
    /// <para>
    /// It does not take the notice off anybody's list: the reading window does not move, so
    /// it still leaves the office inbox twelve hours after it was raised.
    /// </para>
    /// </remarks>
    Task SetArchivedAsync(
        Guid notificationId,
        bool isArchived,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes charge of a notification on behalf of the whole office.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not a display state. Confirming a Will Call tells the patient the office is
    /// arranging their ride, so this must only ever run from an explicit action by a
    /// dispatcher — never from marking something as read.
    /// </remarks>
    Task AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task StopAsync();
}
