namespace Raphael.Desktop.Models;

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
}