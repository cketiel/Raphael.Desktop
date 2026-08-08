using System.Collections.ObjectModel;
using System.Windows;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.ViewModels;

public sealed class NotificationCenterViewModel : BaseViewModel
{
    private readonly Services.NotificationApiClient _apiClient;

    public ObservableCollection<NotificationItemViewModel> Notifications { get; }
        = [];

    private bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public int UnreadCount =>
        Notifications.Count(x => !x.IsViewed);

    public bool HasUnread =>
        UnreadCount > 0;

    public NotificationCenterViewModel(
        Services.NotificationApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var notifications =
            await _apiClient.GetNotificationsAsync(
                cancellationToken);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Notifications.Clear();

            foreach (var notification in notifications)
            {
                Notifications.Add(
                    new NotificationItemViewModel(
                        notification));
            }

            OnPropertyChanged(nameof(UnreadCount));
            OnPropertyChanged(nameof(HasUnread));
        });
    }

    public async Task AddNotificationAsync(
        NotificationDto notification)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var item =
                new NotificationItemViewModel(notification);

            Notifications.Insert(0, item);

            OnPropertyChanged(nameof(UnreadCount));
            OnPropertyChanged(nameof(HasUnread));
        });
    }

    public async Task MarkViewedAsync(
        NotificationItemViewModel notification)
    {
        if (notification.IsViewed)
            return;

        await _apiClient.MarkViewedAsync(
            notification.NotificationRecipientId);

        notification.IsViewed = true;

        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasUnread));
    }

    public async Task MarkAcknowledgedAsync(
        NotificationItemViewModel notification)
    {
        if (notification.IsAcknowledged)
            return;

        await _apiClient.MarkAcknowledgedAsync(
            notification.NotificationRecipientId);

        notification.IsAcknowledged = true;
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
    }
}