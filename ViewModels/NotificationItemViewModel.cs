using Raphael.Desktop.Models;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels;

public sealed class NotificationItemViewModel : BaseViewModel
{
    private bool _isViewed;
    private bool _isAcknowledged;

    public Guid NotificationId { get; }

    public Guid NotificationRecipientId { get; }

    public string BusinessEventCode { get; }

    public string Title { get; }

    public string Message { get; }

    public string Priority { get; }

    public string Severity { get; }

    public string Type { get; }

    public DateTime CreatedAtUtc { get; }

    public List<NotificationActionDto> Actions { get; }

    public bool IsViewed
    {
        get => _isViewed;
        set => SetProperty(ref _isViewed, value);
    }

    public bool IsAcknowledged
    {
        get => _isAcknowledged;
        set => SetProperty(ref _isAcknowledged, value);
    }
  
    public NotificationItemViewModel(
        NotificationDto notification)
    {
        NotificationId = notification.Id;

        var recipient =
            notification.Recipients.FirstOrDefault();

        NotificationRecipientId =
            recipient?.Id ?? Guid.Empty;

        BusinessEventCode =
            notification.BusinessEventCode;

        Title =
            notification.Title;

        Message =
            notification.Message;

        Priority =
            notification.Priority;

        Severity =
            notification.Severity;

        Type =
            notification.Type;

        CreatedAtUtc =
            notification.CreatedAtUtc;

        IsViewed =
            recipient?.IsViewed ?? false;

        IsAcknowledged =
            recipient?.IsAcknowledged ?? false;

        Actions =
            notification.Actions;
    }
}