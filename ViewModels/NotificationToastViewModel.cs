using System.Collections.ObjectModel;
using System.Windows.Input;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;

namespace Raphael.Desktop.ViewModels;

/// <summary>
/// A column of live alert cards.
/// </summary>
/// <remarks>
/// Three at a time and no more. Beyond that the stack stops being something a dispatcher
/// can take in at a glance and becomes a wall over the work area — so the rest collapses
/// into one line that opens the panel. Nothing is ever dropped silently: every notice is
/// already in the inbox and counted on the bell before it gets here.
/// </remarks>
public sealed class NotificationToastLaneViewModel : BaseViewModel
{
    public const int MaxVisible = 3;

    private int _overflowCount;

    public NotificationToastLaneViewModel(Action openCenter)
    {
        OpenCenterCommand = new RelayCommandObject(_ =>
        {
            ClearOverflow();
            openCenter?.Invoke();
        });
    }

    /// <summary>Oldest first, so the newest card sits closest to the corner.</summary>
    public ObservableCollection<NotificationToastItemViewModel> Items { get; } = [];

    public ICommand OpenCenterCommand { get; }

    /// <summary>Raised whenever the lane becomes empty or stops being empty.</summary>
    public event EventHandler Changed;

    public int OverflowCount
    {
        get => _overflowCount;
        private set
        {
            if (!SetProperty(ref _overflowCount, value))
                return;

            OnPropertyChanged(nameof(HasOverflow));
            OnPropertyChanged(nameof(OverflowLabel));
        }
    }

    public bool HasOverflow => OverflowCount > 0;

    public string OverflowLabel => string.Format(
        LocalizationService.Instance["NotificationToastMore"],
        OverflowCount);

    public string CloseText => LocalizationService.Instance["NotificationToastClose"];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Puts a card in, folding it into the one already showing the same trip.
    /// </summary>
    /// <returns>True when a new card appeared, false when it folded into an existing one.</returns>
    public bool Add(NotificationToastItemViewModel item)
    {
        var tripKey = item.TripKey;

        if (!string.IsNullOrWhiteSpace(tripKey))
        {
            var existing = Items.FirstOrDefault(
                candidate => candidate.Level == item.Level &&
                             string.Equals(candidate.TripKey, tripKey, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Merge(item.Dto);

                // The card being folded into was never shown; its clock would keep ticking.
                item.Stop();

                return false;
            }
        }

        Items.Add(item);

        while (Items.Count > MaxVisible)
        {
            var oldest = Items[0];

            Items.RemoveAt(0);
            oldest.Stop();

            OverflowCount++;
        }

        Raise();

        return true;
    }

    public void Remove(NotificationToastItemViewModel item)
    {
        if (item is null)
            return;

        item.Stop();

        if (Items.Remove(item) && Items.Count == 0)
            ClearOverflow();

        Raise();
    }

    public void Clear()
    {
        foreach (var item in Items.ToList())
            item.Stop();

        Items.Clear();

        ClearOverflow();

        Raise();
    }

    private void ClearOverflow() => OverflowCount = 0;

    private void Raise()
    {
        OnPropertyChanged(nameof(IsEmpty));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Decides what a notification arriving in real time is allowed to do to the screen.
/// </summary>
/// <remarks>
/// The dispatcher is on the phone with a patient, routing trips, typing an address. This
/// is the only part of the application that interrupts them, so it works on one rule: a
/// card must be ignorable without anything being lost, and only the one with a patient
/// waiting is allowed to insist.
/// </remarks>
public sealed class NotificationToastViewModel
{
    private readonly NotificationTextService _text;

    private readonly Action<NotificationDto> _open;

    private readonly Func<bool> _isCenterInFront;

    private readonly Action _onActionRequired;

    public NotificationToastViewModel(
        NotificationTextService text,
        Action<NotificationDto> open,
        Action openCenter,
        Func<bool> isCenterInFront,
        Action onActionRequired)
    {
        _text = text;
        _open = open;
        _isCenterInFront = isCenterInFront;
        _onActionRequired = onActionRequired;

        Preferences = NotificationAlertPreferences.Load();

        Inline = new NotificationToastLaneViewModel(openCenter);
        Floating = new NotificationToastLaneViewModel(openCenter);
    }

    public NotificationAlertPreferences Preferences { get; }

    /// <summary>Inside the main window: everything that is not waiting on somebody.</summary>
    public NotificationToastLaneViewModel Inline { get; }

    /// <summary>
    /// The window that floats above every application.
    /// </summary>
    /// <remarks>
    /// Only what needs somebody to act. A Will Call the dispatcher cannot see because they
    /// are in the phone system is a Will Call nobody answers, and there is a patient at the
    /// other end of it. Everything else stays inside Raphael, where it belongs.
    /// </remarks>
    public NotificationToastLaneViewModel Floating { get; }

    public void Show(NotificationDto notification)
    {
        if (notification is null)
            return;

        // Quiet was asked for. It still went into the inbox and still counts on the bell;
        // it just does not take the screen.
        if (Preferences.IsMuted)
            return;

        var level = NotificationAlertLevels.For(notification);

        // No point announcing what the dispatcher is already looking at.
        if (level == NotificationAlertLevel.Ambient && _isCenterInFront?.Invoke() == true)
            return;

        var lane = level == NotificationAlertLevel.ActionRequired
            ? Floating
            : Inline;

        var item = new NotificationToastItemViewModel(
            notification,
            level,
            _text,
            card => lane.Remove(card),
            _open);

        var isNew = lane.Add(item);

        // A trip that folded into a card already on screen does not get a second sound.
        if (isNew && Preferences.ShouldSound(level))
            NotificationAlertPreferences.Play(level);

        if (isNew && level == NotificationAlertLevel.ActionRequired)
            _onActionRequired?.Invoke();
    }

    public void Mute(TimeSpan span)
    {
        Preferences.MuteFor(span);

        Inline.Clear();
        Floating.Clear();
    }

    public void Close()
    {
        Inline.Clear();
        Floating.Clear();
    }
}
