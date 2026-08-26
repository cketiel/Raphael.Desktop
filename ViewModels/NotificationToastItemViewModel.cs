using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.Commands;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;

namespace Raphael.Desktop.ViewModels;

/// <summary>
/// One live alert card.
/// </summary>
/// <remarks>
/// Owns its own clock. The card counts itself down and asks to be removed when it runs
/// out, which is what lets several of them expire on their own schedule instead of the
/// whole stack riding on one shared timer.
/// </remarks>
public sealed class NotificationToastItemViewModel : BaseViewModel
{
    /// <summary>Fine enough for the remaining-time hairline to look continuous.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// After the pointer leaves, the card does not vanish instantly even if its time was
    /// already up: somebody was reading it a moment ago.
    /// </summary>
    private static readonly TimeSpan GraceAfterHover = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan AmbientLife = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan AttentionLife = TimeSpan.FromSeconds(10);

    private readonly NotificationTextService _text;

    private readonly Action<NotificationToastItemViewModel> _dismiss;

    private readonly Action<NotificationDto> _open;

    private readonly DispatcherTimer _clock;

    private TimeSpan _life;

    private TimeSpan _remaining;

    private bool _isPaused;

    private int _count = 1;

    public NotificationToastItemViewModel(
        NotificationDto notification,
        NotificationAlertLevel level,
        NotificationTextService text,
        Action<NotificationToastItemViewModel> dismiss,
        Action<NotificationDto> open)
    {
        Dto = notification;
        Level = level;
        _text = text;
        _dismiss = dismiss;
        _open = open;

        _life = LifeFor(level);
        _remaining = _life;

        OpenCommand = new RelayCommandObject(_ => Open());
        CloseCommand = new RelayCommandObject(_ => _dismiss?.Invoke(this));

        _clock = new DispatcherTimer { Interval = TickInterval };
        _clock.Tick += (_, _) => Tick();
        _clock.Start();
    }

    public NotificationDto Dto { get; private set; }

    public NotificationAlertLevel Level { get; }

    public Guid NotificationId => Dto.Id;

    #region Text

    public string EventName => _text.ResolveEventName(Dto.BusinessEventCode);

    public string Title => _text.Resolve(Dto).Title;

    /// <summary>
    /// ⚠️ Translated here, like the panel does. Reading the DTO's own Title and Message
    /// puts the server's English on screen while the row in the inbox says the same thing
    /// in Spanish.
    /// </summary>
    public string Body => _text.Resolve(Dto).Body;

    /// <summary>
    /// "Viaje 418", or nothing. ⚠️ Never the patient: this card stays on a screen the
    /// whole office can see, and the notification deliberately carries no patient data.
    /// </summary>
    public string TripLabel
    {
        get
        {
            if (!Dto.Metadata.TryGetValue(NotificationKeys.Metadata.TripId, out var tripId) ||
                string.IsNullOrWhiteSpace(tripId))
            {
                return string.Empty;
            }

            return $"{LocalizationService.Instance["NotificationTrip"]} {tripId}";
        }
    }

    public bool HasTrip => !string.IsNullOrEmpty(TripLabel);

    /// <summary>The trip this card is about, for folding repeats into it.</summary>
    public string TripKey =>
        Dto.Metadata.TryGetValue(NotificationKeys.Metadata.TripId, out var tripId)
            ? tripId
            : null;

    /// <summary>The dispatcher's own clock: the notification stores an instant in UTC.</summary>
    public string TimeLabel =>
        DateTime.SpecifyKind(Dto.CreatedAtUtc, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString("t");

    #endregion

    #region Look

    public Brush AccentBrush =>
        NotificationSeverityPalette.Foreground(Dto.Severity);

    public Brush IconBackground =>
        NotificationSeverityPalette.Background(Dto.Severity);

    public PackIconKind Icon =>
        NotificationSeverityPalette.Icon(Dto.Severity);

    /// <summary>
    /// One line and no message body: the operation moving forward is worth a glance, not a
    /// paragraph.
    /// </summary>
    public bool IsCompact => Level == NotificationAlertLevel.Ambient;

    public bool ShowBody => !IsCompact;

    /// <summary>
    /// How solid the card's backing plate is when nobody is pointing at it.
    /// </summary>
    /// <remarks>
    /// ⚠️ The transparency lives in this plate, never in the card's own Opacity: fading the
    /// element fades the text with it, and a washed-out message is harder to read than a
    /// solid one — the opposite of what a translucent alert is for. The plate sits behind
    /// the content as a sibling, so the text stays at full strength on top of it.
    /// </remarks>
    public double PlateOpacity =>
        Level == NotificationAlertLevel.ActionRequired ? 0.95 : 0.91;

    #endregion

    #region Repetition

    /// <summary>
    /// How many notices of the same trip this card stands for.
    /// </summary>
    /// <remarks>
    /// Started, arrived, on board and completed inside two minutes is one trip moving, not
    /// four interruptions. They fold into the card already on screen.
    /// </remarks>
    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(HasCount));
        }
    }

    public bool HasCount => Count > 1;

    public void Merge(NotificationDto newer)
    {
        if (newer is null)
            return;

        Dto = newer;

        Count++;

        _remaining = _life;

        OnPropertyChanged(nameof(EventName));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(TimeLabel));
        OnPropertyChanged(nameof(AccentBrush));
        OnPropertyChanged(nameof(IconBackground));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Progress));
        RefreshCountdown();
    }

    #endregion

    #region The clock

    /// <summary>Fraction of its life the card still has, for the hairline.</summary>
    public double Progress =>
        _life <= TimeSpan.Zero
            ? 0
            : Math.Clamp(_remaining.TotalMilliseconds / _life.TotalMilliseconds, 0, 1);

    /// <summary>
    /// False for what needs somebody to act: that card stays until it is dealt with.
    /// </summary>
    public bool HasProgress => _life > TimeSpan.Zero;

    /// <summary>
    /// Holds the card while the pointer is on it. Nothing is worse than a notice that
    /// disappears halfway through the sentence being read.
    /// </summary>
    public void Pause() => _isPaused = true;

    public void Resume()
    {
        _isPaused = false;

        if (HasProgress && _remaining < GraceAfterHover)
        {
            _remaining = GraceAfterHover;
            OnPropertyChanged(nameof(Progress));
        }
    }

    public void Stop() => _clock.Stop();

    private void Tick()
    {
        RefreshCountdown();

        if (!HasProgress || _isPaused)
            return;

        _remaining -= TickInterval;

        OnPropertyChanged(nameof(Progress));

        if (_remaining <= TimeSpan.Zero)
        {
            _clock.Stop();
            _dismiss?.Invoke(this);
        }
    }

    private static TimeSpan LifeFor(NotificationAlertLevel level) =>
        level switch
        {
            // Zero means it never leaves on its own.
            NotificationAlertLevel.ActionRequired => TimeSpan.Zero,
            NotificationAlertLevel.Attention => AttentionLife,
            _ => AmbientLife
        };

    #endregion

    #region Will Call countdown

    public DateTime? DeadlineUtc =>
        Dto.Metadata.TryGetValue(NotificationKeys.Metadata.WillCallDeadlineUtc, out var raw) &&
        NotificationTextService.TryParseUtc(raw, out var deadline)
            ? deadline
            : null;

    public bool HasCountdown => DeadlineUtc.HasValue;

    /// <summary>
    /// Counted from the moment the patient said they were ready, not from the moment
    /// somebody looked at the screen.
    /// </summary>
    public string CountdownText
    {
        get
        {
            if (!DeadlineUtc.HasValue)
                return string.Empty;

            var left = DeadlineUtc.Value - DateTime.UtcNow;

            if (left <= TimeSpan.Zero)
                return LocalizationService.Instance["NotificationOverdue"];

            return left.TotalHours >= 1
                ? $"{(int)left.TotalHours}h {left.Minutes:D2}m"
                : $"{(int)left.TotalMinutes}m";
        }
    }

    public Brush CountdownBrush
    {
        get
        {
            if (!DeadlineUtc.HasValue)
                return Brushes.Transparent;

            var left = DeadlineUtc.Value - DateTime.UtcNow;

            if (left <= TimeSpan.Zero)
                return NotificationSeverityPalette.Foreground(NotificationKeys.Severity.Critical);

            return left <= TimeSpan.FromMinutes(15)
                ? NotificationSeverityPalette.Foreground(NotificationKeys.Severity.Error)
                : NotificationSeverityPalette.Foreground(NotificationKeys.Severity.Warning);
        }
    }

    private void RefreshCountdown()
    {
        if (!HasCountdown)
            return;

        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(CountdownBrush));
    }

    #endregion

    public ICommand OpenCommand { get; }

    public ICommand CloseCommand { get; }

    private void Open()
    {
        _open?.Invoke(Dto);
        _dismiss?.Invoke(this);
    }
}
