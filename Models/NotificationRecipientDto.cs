namespace Raphael.Desktop.Models;

/// <summary>
/// The row that says this notification was addressed to us.
/// </summary>
/// <remarks>
/// Copy of <c>Raphael.Notification.Application.DTOs.NotificationRecipientDto</c>.
/// Raphael.Shared is not shared with client applications, so this class is kept in step
/// by hand. See <c>_meta/CONTRACT_MAP.md</c>.
///
/// <para>
/// ⚠️ It used to declare <c>IsViewed</c> and <c>IsAcknowledged</c> as plain properties.
/// The server never sends those names — it sends the timestamps — so they deserialized
/// to <c>false</c> forever and the bell counted every notification as unread for the
/// lifetime of the session. They are computed from the timestamps now.
/// </para>
/// </remarks>
public sealed class NotificationRecipientDto
{
    public Guid Id { get; set; }

    public Guid RecipientId { get; set; }

    public string RecipientType { get; set; } = string.Empty;

    /// <summary>
    /// True when the notification addresses the whole dispatch office instead of one
    /// dispatcher.
    /// </summary>
    /// <remarks>
    /// An office notice is stored once and read by everyone, so its viewed mark belongs
    /// to the office. Marking a broadcast as viewed clears the unread state for every
    /// other dispatcher, which is why Raphael.Desktop keeps read state of its own and
    /// only reports <c>view</c> for notices addressed to one person.
    /// </remarks>
    public bool IsBroadcast { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime? DeliveredAtUtc { get; set; }

    public DateTime? ViewedAtUtc { get; set; }

    public DateTime? AcknowledgedAtUtc { get; set; }

    public bool IsViewed => ViewedAtUtc.HasValue;

    public bool IsAcknowledged => AcknowledgedAtUtc.HasValue;
}
