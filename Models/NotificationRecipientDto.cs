namespace Raphael.Desktop.Models;

public sealed class NotificationRecipientDto
{
    public Guid Id { get; set; }

    public Guid RecipientId { get; set; }

    public string RecipientType { get; set; } = string.Empty;

    public bool IsViewed { get; set; }

    public bool IsAcknowledged { get; set; }

    public DateTime? ViewedAtUtc { get; set; }

    public DateTime? AcknowledgedAtUtc { get; set; }
}