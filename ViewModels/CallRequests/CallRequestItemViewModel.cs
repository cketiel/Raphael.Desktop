using System.Windows.Media;
using Raphael.Desktop.DTOs;
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
        Dto.OperatingDate.Date == DateTime.Today;

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

    public bool IsFromEarlierDay => IsOpen && Dto.OperatingDate.Date < DateTime.Today;

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

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
