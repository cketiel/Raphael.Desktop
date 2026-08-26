namespace Raphael.Desktop.Models;

/// <summary>
/// An archived notification, as the administration area lists it.
/// </summary>
/// <remarks>
/// Copy of <c>Raphael.Notification.Application.DTOs.ArchivedNotificationDto</c>, kept in
/// step by hand. See <c>_meta/CONTRACT_MAP.md</c>.
/// </remarks>
public sealed class ArchivedNotificationDto
{
    public Guid Id { get; set; }

    public string BusinessEventCode { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public List<string> Audiences { get; set; } = [];

    public string? ArchivedByUsername { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }
}

/// <summary>The archived notifications of one application.</summary>
public sealed class ArchivedNotificationGroupDto
{
    public string Audience { get; set; } = string.Empty;

    public int Count { get; set; }

    public List<ArchivedNotificationDto> Items { get; set; } = [];
}

/// <summary>
/// Everything archived, grouped by the application it was addressed to.
/// </summary>
/// <remarks>
/// ⚠️ <see cref="Total"/> is not the sum of the groups: one notification can be addressed
/// to several applications and appears under each while being one row.
/// </remarks>
public sealed class ArchivedNotificationsDto
{
    public int Total { get; set; }

    public List<ArchivedNotificationGroupDto> Groups { get; set; } = [];
}

/// <summary>One entry of the notification administration trail.</summary>
public sealed class NotificationAdminAuditDto
{
    public Guid Id { get; set; }

    public string Action { get; set; } = string.Empty;

    public int? PerformedByUserId { get; set; }

    public string PerformedByUsername { get; set; } = string.Empty;

    public DateTime PerformedAtUtc { get; set; }

    public Guid? NotificationId { get; set; }

    public int AffectedCount { get; set; }

    public string? Details { get; set; }
}
