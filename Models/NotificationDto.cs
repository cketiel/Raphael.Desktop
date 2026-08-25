namespace Raphael.Desktop.Models;

/// <summary>
/// One notification as the API serves it.
/// </summary>
/// <remarks>
/// Copy of <c>Raphael.Notification.Application.DTOs.NotificationDto</c>, kept in step by
/// hand. See <c>_meta/CONTRACT_MAP.md</c>.
///
/// <para>
/// <see cref="Title"/> and <see cref="Message"/> are the English text the server rendered.
/// The panel does not show them directly: it renders from <see cref="Metadata"/> in the
/// language the dispatcher picked, and falls back to these two when a key is missing.
/// </para>
/// </remarks>
public sealed class NotificationDto
{
    public Guid Id { get; set; }

    public string BusinessEventCode { get; set; } = string.Empty;

    public string Priority { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public List<NotificationRecipientDto> Recipients { get; set; }
        = [];

    public List<NotificationActionDto> Actions { get; set; }
        = [];

    /// <summary>
    /// Message key, its parameters and the identifiers the notification is about.
    /// Keys are the ones declared in <c>NotificationMetadataKeys</c> on the server.
    /// Never contains patient data.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; }
        = [];
}
