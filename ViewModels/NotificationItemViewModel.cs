using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;

namespace Raphael.Desktop.ViewModels;

/// <summary>
/// One row of the Notification Center.
/// </summary>
public sealed class NotificationItemViewModel : BaseViewModel
{
    /// <summary>Under this much time left, the Will Call countdown turns red.</summary>
    private static readonly TimeSpan CriticalWindow = TimeSpan.FromMinutes(10);

    /// <summary>Under this much time left, it turns amber.</summary>
    private static readonly TimeSpan WarningWindow = TimeSpan.FromMinutes(30);

    private static readonly Brush UnreadRowBrush = Frozen(Colors.White);

    private static readonly Brush ReadRowBrush = Frozen(Color.FromRgb(0xF2, 0xF6, 0xFC));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private readonly NotificationTextService _text;

    private string _cachedLanguage;
    private string _title;
    private string _body;

    private bool _isRead;

    public NotificationDto Dto { get; private set; }

    public Guid NotificationId => Dto.Id;

    public string BusinessEventCode => Dto.BusinessEventCode;

    public string Severity => Dto.Severity;

    public DateTime CreatedAtUtc => Dto.CreatedAtUtc;

    public DateTime CreatedAtLocal =>
        DateTime.SpecifyKind(Dto.CreatedAtUtc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>
    /// The recipient row this dispatcher can act on, or null when the payload arrived
    /// without one.
    /// </summary>
    public NotificationRecipientDto Recipient => Dto.Recipients.FirstOrDefault();

    public NotificationItemViewModel(
        NotificationDto notification,
        NotificationTextService text,
        bool isRead)
    {
        Dto = notification;
        _text = text;
        _isRead = isRead;
    }

    #region Text

    public string Title
    {
        get
        {
            EnsureText();
            return _title;
        }
    }

    public string Body
    {
        get
        {
            EnsureText();
            return _body;
        }
    }

    public string EventName =>
        _text.ResolveEventName(Dto.BusinessEventCode);

    /// <summary>
    /// Re-resolves the text only when the language actually changed. BaseViewModel raises
    /// PropertyChanged for everything on every language switch, so these getters are hit
    /// often and must not translate the whole inbox each time.
    /// </summary>
    private void EnsureText()
    {
        var language = LocalizationService.Instance.CurrentLanguage;

        if (_cachedLanguage == language && _title is not null)
            return;

        (_title, _body) = _text.Resolve(Dto);

        _cachedLanguage = language;
    }

    #endregion

    #region Read and acknowledged

    /// <summary>
    /// Personal to this dispatcher. Set by the service, never written straight from a binding.
    /// </summary>
    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (!SetProperty(ref _isRead, value))
                return;

            OnPropertyChanged(nameof(FontWeight));
            OnPropertyChanged(nameof(RowBackground));
            OnPropertyChanged(nameof(ReadToggleIcon));
            OnPropertyChanged(nameof(ReadToggleTooltip));
        }
    }

    public FontWeight FontWeight =>
        IsRead ? FontWeights.Normal : FontWeights.Bold;

    /// <summary>
    /// Unread rows are white and read rows are shaded, the way a mail client separates
    /// them at a glance without needing to read a word.
    /// </summary>
    public Brush RowBackground =>
        IsRead ? ReadRowBrush : UnreadRowBrush;

    /// <summary>
    /// The icon of the read toggle shows what pressing it will do, not what the row is.
    /// </summary>
    public PackIconKind ReadToggleIcon =>
        IsRead
            ? PackIconKind.EmailOutline        // press to make it unread again
            : PackIconKind.EmailOpenOutline;   // press to mark it read

    public string ReadToggleTooltip =>
        IsRead
            ? LocalizationService.Instance["NotificationMarkUnread"]
            : LocalizationService.Instance["NotificationMarkRead"];

    /// <summary>
    /// Kept for good: the cleanup will never expire or delete this record.
    /// </summary>
    /// <remarks>
    /// Server truth, not a local preference — it holds for every application. Read from the
    /// notification's own status rather than tracked separately, so it cannot drift.
    /// </remarks>
    public bool IsArchived =>
        string.Equals(
            Dto.Status,
            NotificationKeys.Status.Archived,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The icon shows what pressing it will do, not what the row is.</summary>
    public PackIconKind ArchiveToggleIcon =>
        IsArchived
            ? PackIconKind.ArchiveArrowUpOutline    // press to let it age normally again
            : PackIconKind.ArchiveArrowDownOutline; // press to keep it

    public string ArchiveToggleTooltip =>
        LocalizationService.Instance[
            IsArchived
                ? "NotificationUnarchive"
                : "NotificationArchive"];

    public void RefreshArchived()
    {
        OnPropertyChanged(nameof(IsArchived));
        OnPropertyChanged(nameof(ArchiveToggleIcon));
        OnPropertyChanged(nameof(ArchiveToggleTooltip));
    }

    public bool IsAcknowledged =>
        Recipient?.IsAcknowledged ?? false;

    /// <summary>Still waiting for somebody in the office to take charge.</summary>
    public bool RequiresAction =>
        NotificationKeys.RequiresAction(
            Dto.Type,
            Dto.BusinessEventCode,
            IsAcknowledged);

    /// <summary>Can be acted on, and nobody has.</summary>
    public bool CanAcknowledge =>
        Recipient is not null &&
        Recipient.Id != Guid.Empty &&
        !Recipient.IsAcknowledged;

    public string StatusLabel =>
        IsAcknowledged
            ? LocalizationService.Instance["NotificationAcknowledged"]
            : LocalizationService.Instance["NotificationPending"];

    /// <summary>
    /// Colours of the confirmed badge, taken from the palette every other notice uses so a
    /// fifth hand-picked pair cannot drift away from the rest.
    /// </summary>
    public Brush AcknowledgedForeground =>
        NotificationSeverityPalette.Foreground(NotificationKeys.Severity.Success);

    public Brush AcknowledgedBackground =>
        NotificationSeverityPalette.Background(NotificationKeys.Severity.Success);

    #endregion

    #region Severity

    public Brush SeverityBrush =>
        NotificationSeverityPalette.Foreground(Dto.Severity);

    public Brush SeverityBackground =>
        NotificationSeverityPalette.Background(Dto.Severity);

    public PackIconKind SeverityIcon =>
        NotificationSeverityPalette.Icon(Dto.Severity);

    #endregion

    #region Trip

    /// <summary>
    /// The trip this notification is about, as the server's own message cites it. This is
    /// the internal identifier; the broker's trip number lives on the trip itself and only
    /// appears once the detail pane has loaded it.
    /// </summary>
    public int? TripId =>
        Dto.Metadata.TryGetValue(NotificationKeys.Metadata.TripId, out var raw) &&
        int.TryParse(raw, out var id)
            ? id
            : null;

    public string TripLabel =>
        TripId?.ToString() ?? string.Empty;

    public int? RiderId =>
        Dto.Metadata.TryGetValue(NotificationKeys.Metadata.RiderId, out var raw) &&
        int.TryParse(raw, out var id)
            ? id
            : null;

    private string _brokerTripNumber;

    /// <summary>
    /// The trip number used with funding sources and brokers (<c>Trip.TripId</c>), filled
    /// in once the trip has been loaded.
    /// </summary>
    /// <remarks>
    /// It does not travel with the notification — only the internal identifier does — but
    /// it is what a dispatcher has in hand when a broker calls about a trip, so the search
    /// box has to find it. Populated from the trip cache as the detail pane loads trips.
    /// </remarks>
    public string BrokerTripNumber
    {
        get => _brokerTripNumber;
        set
        {
            if (SetProperty(ref _brokerTripNumber, value))
                OnPropertyChanged(nameof(TripDisplay));
        }
    }

    public string TripDisplay =>
        string.IsNullOrWhiteSpace(BrokerTripNumber)
            ? TripLabel
            : $"{TripLabel} · {BrokerTripNumber}";

    #endregion

    #region Thread

    private IReadOnlyList<NotificationItemViewModel> _thread = [];

    /// <summary>
    /// Every notice about this same trip, oldest first, when the list is grouped.
    /// </summary>
    /// <remarks>
    /// A single trip generates a run of notices — started, arrived, on board, completed —
    /// and read one by one they bury everything else. Collapsed into one row they read as
    /// what they are: the story of one journey. This row is the newest of them and speaks
    /// for the rest, exactly as a mail client's conversation does.
    /// </remarks>
    public IReadOnlyList<NotificationItemViewModel> Thread
    {
        get => _thread;
        set
        {
            _thread = value ?? [];

            OnPropertyChanged(nameof(Thread));
            OnPropertyChanged(nameof(ThreadCount));
            OnPropertyChanged(nameof(HasThread));
            OnPropertyChanged(nameof(ThreadSummary));
        }
    }

    public int ThreadCount => Thread.Count;

    public bool HasThread => ThreadCount > 1;

    /// <summary>
    /// "Iniciado → Llegó → A bordo", the trip's story in one line.
    /// </summary>
    public string ThreadSummary
    {
        get
        {
            if (!HasThread)
                return string.Empty;

            return string.Join(
                " → ",
                Thread.Select(item => _text.ResolveEventName(item.BusinessEventCode)));
        }
    }

    #endregion

    #region Time and Will Call countdown

    /// <summary>
    /// Time alone for today, date and time for anything older. An office notice lives
    /// twelve hours, so the list routinely straddles midnight.
    /// </summary>
    public string TimeLabel =>
        CreatedAtLocal.Date == DateTime.Today
            ? CreatedAtLocal.ToString("t")
            : CreatedAtLocal.ToString("g");

    public DateTime? WillCallDeadlineUtc =>
        Dto.Metadata.TryGetValue(
            NotificationKeys.Metadata.WillCallDeadlineUtc,
            out var raw) &&
        NotificationTextService.TryParseUtc(raw, out var deadline)
            ? deadline
            : null;

    public bool HasCountdown =>
        WillCallDeadlineUtc.HasValue && !IsAcknowledged;

    public TimeSpan? TimeLeft =>
        WillCallDeadlineUtc.HasValue
            ? WillCallDeadlineUtc.Value - DateTime.UtcNow
            : null;

    public bool IsOverdue =>
        TimeLeft.HasValue && TimeLeft.Value <= TimeSpan.Zero;

    /// <summary>
    /// How long the office has left to get a vehicle to the patient.
    /// </summary>
    /// <remarks>
    /// The hour is counted from the moment the patient said they were ready, not from the
    /// moment somebody looked at the screen. A notice that sat unread for forty minutes
    /// shows twenty left, and that is the truth of it.
    /// </remarks>
    public string CountdownText
    {
        get
        {
            var left = TimeLeft;

            if (!left.HasValue)
                return string.Empty;

            if (left.Value <= TimeSpan.Zero)
                return LocalizationService.Instance["NotificationOverdue"];

            return left.Value.TotalHours >= 1
                ? $"{(int)left.Value.TotalHours}h {left.Value.Minutes:D2}m"
                : $"{(int)left.Value.TotalMinutes}m";
        }
    }

    public Brush CountdownBrush
    {
        get
        {
            var left = TimeLeft;

            if (!left.HasValue)
                return Brushes.Transparent;

            if (left.Value <= TimeSpan.Zero)
                return NotificationSeverityPalette.Foreground(
                    NotificationKeys.Severity.Critical);

            if (left.Value <= CriticalWindow)
                return NotificationSeverityPalette.Foreground(
                    NotificationKeys.Severity.Error);

            if (left.Value <= WarningWindow)
                return NotificationSeverityPalette.Foreground(
                    NotificationKeys.Severity.Warning);

            return NotificationSeverityPalette.Foreground(
                NotificationKeys.Severity.Success);
        }
    }

    /// <summary>Called on every tick of the panel's clock.</summary>
    public void RefreshCountdown()
    {
        if (!WillCallDeadlineUtc.HasValue)
            return;

        OnPropertyChanged(nameof(TimeLeft));
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(CountdownBrush));
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(HasCountdown));
    }

    #endregion

    /// <summary>
    /// Takes on the server's latest copy of this notification, keeping the row itself so
    /// the dispatcher does not lose their place or their selection.
    /// </summary>
    /// <remarks>
    /// ⚠️ A reload replaces every payload in the service with a freshly deserialised
    /// object. Without adopting it, the row went on pointing at the instance it was built
    /// from, and the two copies drifted apart: acknowledging wrote the timestamp on the
    /// service's copy while <see cref="CanAcknowledge"/> read this one. The Confirm button
    /// stayed enabled on a Will Call that had already been confirmed — and pressing it
    /// again promises the same patient a second time that a vehicle is on its way.
    /// </remarks>
    public void Adopt(NotificationDto fresh)
    {
        if (fresh is null || ReferenceEquals(fresh, Dto))
            return;

        Dto = fresh;

        // The rendered text belongs to the payload that was just replaced.
        _cachedLanguage = null;
        _title = null;
        _body = null;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(EventName));
        OnPropertyChanged(nameof(CreatedAtLocal));
    }

    /// <summary>Called when the acknowledged state changed underneath.</summary>
    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsAcknowledged));
        OnPropertyChanged(nameof(CanAcknowledge));
        OnPropertyChanged(nameof(RequiresAction));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(HasCountdown));
    }

    /// <summary>
    /// True when the row matches what the dispatcher typed. Matches the internal trip
    /// identifier as well as the notification text, so pasting a trip number from another
    /// system finds it.
    /// </summary>
    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var term = search.Trim();

        return Contains(TripLabel, term)
               || Contains(BrokerTripNumber, term)
               || Contains(Title, term)
               || Contains(Body, term)
               || Contains(EventName, term);
    }

    private static bool Contains(string value, string term) =>
        !string.IsNullOrEmpty(value) &&
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
