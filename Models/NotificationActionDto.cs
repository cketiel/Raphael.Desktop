namespace Raphael.Desktop.Models;

public sealed class NotificationActionDto
{
    public string ActionCode { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsPrimary { get; set; }
}