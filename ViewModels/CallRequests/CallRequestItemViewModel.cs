using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.CallRequests;

namespace Raphael.Desktop.ViewModels.CallRequests;

/// <summary>One row of the call-back queue.</summary>
/// <remarks>
/// Kept and updated in place by id rather than rebuilt: the base view model subscribes to a static
/// event, and the selection has to survive every live message.
/// </remarks>
public sealed class CallRequestItemViewModel : BaseViewModel
{
    /// <summary>A driver waiting this long turns the row amber.</summary>
    public static readonly TimeSpan AmberAfter = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan RedAfter = TimeSpan.FromMinutes(10);

    /// <summary>A case held this long without being closed is flagged to everybody, so someone can take over.</summary>
    public static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(10);

    private static readonly Brush Neutral = Frozen("#5F6368");
    private static readonly Brush Amber = Frozen("#B06000");
    private static readonly Brush Red = Frozen("#D93025");
    private static readonly Brush Accent = Frozen("#673AB7");
    private static readonly Brush Green = Frozen("#188038");

    public CallRequestItemViewModel(CallRequestSummaryDto dto)
    {
        Dto = dto;
    }

    private static LocalizationService L => LocalizationService.Instance;

    public CallRequestSummaryDto Dto { get; private set; }

    public int Id => Dto.Id;

    public void Update(CallRequestSummaryDto dto)
    {
        Dto = dto;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Moves the clocks on screen. Called every thirty seconds.</summary>
    public void Tick()
    {
        OnPropertyChanged(nameof(WaitingText));
        OnPropertyChanged(nameof(WaitingBrush));
        OnPropertyChanged(nameof(IsStaleClaim));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(ClaimAgeText));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StateBackground));
    }

    #region State

    public bool IsWaiting => Dto.Status == CallRequestStatuses.Waiting;

    public bool IsInProgress => Dto.Status == CallRequestStatuses.InProgress;

    public bool IsOpen => IsWaiting || IsInProgress;

    public bool IsClosed => !IsOpen;

    public bool IsMine => IsInProgress && Dto.ClaimedByUserId == CallRequestBoard.CurrentUserId;

    public bool IsHeldByOther => IsInProgress && !IsMine;

    public bool CanReopen =>
        (Dto.Status == CallRequestStatuses.Resolved || Dto.Status == CallRequestStatuses.Cancelled) &&
        Dto.OperatingDate.Date == BusinessDay.Today;

    public bool IsStaleClaim =>
        IsInProgress &&
        Dto.ClaimedAtUtc.HasValue &&
        DateTime.UtcNow - CallRequestText.Utc(Dto.ClaimedAtUtc.Value) >= StaleClaimAfter;

    /// <summary>The office tried to call and the driver has not said they can talk since.</summary>
    public bool HasMissedCall =>
        IsInProgress &&
        Dto.LastAttemptAtUtc.HasValue &&
        (Dto.DriverAvailableAtUtc is null || Dto.DriverAvailableAtUtc < Dto.LastAttemptAtUtc);

    /// <summary>The driver pressed "I can talk now" after the last failed call.</summary>
    public bool DriverCanTalk =>
        IsInProgress &&
        Dto.DriverAvailableAtUtc.HasValue &&
        (Dto.LastAttemptAtUtc is null || Dto.DriverAvailableAtUtc > Dto.LastAttemptAtUtc);

    #endregion

    #region Text

    public string DriverName => Dto.DriverName;

    public bool HasRoute => Dto.VehicleRouteId.HasValue;

    public string RouteText =>
        string.IsNullOrWhiteSpace(Dto.RouteName) ? L["CallRequestNoRoute"] : Dto.RouteName!;

    public string RequestedAtText => CallRequestText.ExactTime(Dto.RequestedAtUtc);

    /// <summary>How long the driver has waited, while open; how long the case lasted, once closed.</summary>
    public TimeSpan Waiting =>
        (IsOpen ? DateTime.UtcNow : CallRequestText.Utc(Dto.ClosedAtUtc ?? DateTime.UtcNow))
        - CallRequestText.Utc(Dto.QueuedAtUtc);

    public string WaitingText =>
        IsOpen ? CallRequestText.Ago(Dto.QueuedAtUtc) : CallRequestText.Duration(Waiting);

    public Brush WaitingBrush =>
        !IsWaiting ? Neutral
        : Waiting >= RedAfter ? Red
        : Waiting >= AmberAfter ? Amber
        : Neutral;

    public bool HasReminders => Dto.ReminderCount > 0;

    public string ReminderText => $"×{Dto.ReminderCount}";

    public string ReminderTooltip =>
        HasReminders
            ? string.Format(L["CallRequestReminderTooltip"], Dto.ReminderCount, CallRequestText.ShortTime(Dto.LastReminderAtUtc))
            : string.Empty;

    public bool IsRepeatOfDay => Dto.RequestNumberOfDay > 1;

    public string NumberOfDayText => string.Format(L["CallRequestNthToday"], Dto.RequestNumberOfDay);

    public bool IsFromEarlierDay => IsOpen && Dto.OperatingDate.Date < BusinessDay.Today;

    public string EarlierDayText =>
        string.Format(L["CallRequestFromDay"], Dto.OperatingDate.ToString("d", CallRequestText.Culture));

    public string ClaimAgeText => CallRequestText.Ago(Dto.ClaimedAtUtc);

    public string AttemptsText =>
        string.Format(L["CallRequestAttempts"], Dto.CallAttempts, CallRequestText.ShortTime(Dto.LastAttemptAtUtc));

    public string DriverCanTalkText =>
        string.Format(L["CallRequestDriverCanTalk"], CallRequestText.ShortTime(Dto.DriverAvailableAtUtc));

    public string StatusText => StatusTextFor(Dto);

    public static string StatusTextFor(CallRequestSummaryDto dto) => dto.Status switch
    {
        CallRequestStatuses.Waiting => L["CallRequestStatusWaiting"],
        CallRequestStatuses.InProgress => string.Format(
            L["CallRequestStatusInProgress"],
            dto.ClaimedByUserId == CallRequestBoard.CurrentUserId ? L["CallRequestYou"] : dto.ClaimedByName,
            CallRequestText.ShortTime(dto.ClaimedAtUtc)),
        CallRequestStatuses.Resolved => string.Format(
            L["CallRequestStatusResolved"],
            dto.ResolvedByName,
            CallRequestText.ShortTime(dto.ClosedAtUtc)),
        CallRequestStatuses.Cancelled => L["CallRequestStatusCancelled"],
        CallRequestStatuses.Expired => L["CallRequestStatusExpired"],
        _ => dto.Status
    };

    public Brush StatusBrush =>
        IsWaiting ? WaitingBrush
        : IsInProgress ? (IsStaleClaim ? Amber : IsMine ? Green : Accent)
        : Neutral;

    public bool HasReason => !string.IsNullOrWhiteSpace(Dto.ReasonCode);

    public string ReasonText => CallRequestText.Reason(Dto.ReasonCode);

    #endregion

    #region How the row looks

    private CallRequestGroup? _group;

    /// <summary>The heading the row sits under. Set by the panel, which owns the headings.</summary>
    public CallRequestGroup? Group
    {
        get => _group;
        set => SetProperty(ref _group, value);
    }

    /// <summary>
    /// The row's place in the queue, as one string the list can sort on: waiting by arrival,
    /// being handled by when it was taken, closed with the latest first.
    /// </summary>
    public string SortKey
    {
        get
        {
            if (IsWaiting)
                return $"0{CallRequestText.Utc(Dto.QueuedAtUtc).Ticks:D20}";

            if (IsInProgress)
                return $"1{CallRequestText.Utc(Dto.ClaimedAtUtc ?? Dto.QueuedAtUtc).Ticks:D20}";

            var closed = CallRequestText.Utc(Dto.ClosedAtUtc ?? Dto.QueuedAtUtc).Ticks;

            return $"2{DateTime.MaxValue.Ticks - closed:D20}";
        }
    }

    /// <summary>
    /// The state as one colour, in the language the notification rows already speak: red for a
    /// driver kept waiting too long or a call that did not connect, amber for one getting there,
    /// green for a driver who can talk or a closed case, purple for somebody in the office on it.
    /// </summary>
    private string Tone =>
        HasMissedCall ? NotificationKeys.Severity.Error
        : DriverCanTalk ? NotificationKeys.Severity.Success
        : IsWaiting ? (Waiting >= RedAfter ? NotificationKeys.Severity.Error
                     : Waiting >= AmberAfter ? NotificationKeys.Severity.Warning
                     : NotificationKeys.Severity.Information)
        : IsInProgress ? (IsStaleClaim ? NotificationKeys.Severity.Warning : AccentTone)
        : Dto.Status == CallRequestStatuses.Resolved ? NotificationKeys.Severity.Success
        : NeutralTone;

    private const string AccentTone = "Accent";
    private const string NeutralTone = "Neutral";

    private static readonly Brush AccentSurface = Frozen("#EDE7F6");
    private static readonly Brush NeutralSurface = Frozen("#F1F3F4");
    private static readonly Brush MineRow = Frozen("#F7F4FC");

    public Brush StateBrush => Tone switch
    {
        AccentTone => Accent,
        NeutralTone => Neutral,
        var tone => NotificationSeverityPalette.Foreground(tone)
    };

    public Brush StateBackground => Tone switch
    {
        AccentTone => AccentSurface,
        NeutralTone => NeutralSurface,
        var tone => NotificationSeverityPalette.Background(tone)
    };

    public PackIconKind StateIcon =>
        HasMissedCall ? PackIconKind.PhoneMissed
        : DriverCanTalk ? PackIconKind.PhoneRing
        : IsWaiting ? PackIconKind.PhoneClock
        : IsMine ? PackIconKind.PhoneInTalk
        : IsInProgress ? PackIconKind.Headset
        : Dto.Status == CallRequestStatuses.Resolved ? PackIconKind.PhoneCheck
        : Dto.Status == CallRequestStatuses.Cancelled ? PackIconKind.PhoneCancel
        : PackIconKind.PhoneOff;

    /// <summary>The dispatcher's own cases are tinted, the way the inbox tints what is unread.</summary>
    public Brush RowBackground => IsMine ? MineRow : Brushes.Transparent;

    /// <summary>A driver still waiting is the row that needs somebody: it reads heavier.</summary>
    public FontWeight NameWeight => IsWaiting ? FontWeights.SemiBold : FontWeights.Normal;

    #endregion

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
