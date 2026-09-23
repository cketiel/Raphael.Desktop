using System.Collections.ObjectModel;
using System.Windows.Media;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.CallRequests;

namespace Raphael.Desktop.ViewModels.CallRequests;

/// <summary>
/// Everything a dispatcher needs in front of them before calling the driver back.
/// </summary>
/// <remarks>
/// The point of the whole feature is that the call starts with the answer already on screen:
/// how the driver's day is going, the stop they are on and how late it runs, where the vehicle is,
/// and what they asked earlier today.
/// </remarks>
public sealed class CallRequestDetailViewModel : BaseViewModel
{
    private static readonly Brush LateBrush = Frozen("#D93025");
    private static readonly Brush OnTimeBrush = Frozen("#188038");

    private readonly ICallRequestApiClient _api;

    private CallRequestDetailDto? _dto;

    private int _version;

    private bool _isLoading;

    private bool _failed;

    public CallRequestDetailViewModel(ICallRequestApiClient api)
    {
        _api = api;
    }

    private static LocalizationService L => LocalizationService.Instance;

    public ObservableCollection<CallRequestTimelineLine> Timeline { get; } = [];

    public ObservableCollection<string> OtherRequests { get; } = [];

    public int? LoadedId => _dto?.Request.Id;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public async Task LoadAsync(int id)
    {
        // Only the last request asked for may land: a slow answer for the row the dispatcher
        // left must not overwrite the one they are looking at.
        var version = ++_version;

        IsLoading = true;

        try
        {
            var dto = await _api.GetDetailAsync(id);

            if (version != _version)
                return;

            _failed = dto is null;
            Set(dto);
        }
        catch (Exception ex)
        {
            if (version != _version)
                return;

            _failed = true;
            FileLogger.Log($"Call requests: could not load request {id}. {ex.Message}");
            Set(null);
        }
        finally
        {
            if (version == _version)
                IsLoading = false;
        }
    }

    public void Clear()
    {
        _version++;
        _failed = false;
        IsLoading = false;
        Set(null);
    }

    private void Set(CallRequestDetailDto? dto)
    {
        _dto = dto;

        Timeline.Clear();
        OtherRequests.Clear();

        if (dto is not null)
        {
            foreach (var line in dto.Timeline)
                Timeline.Add(new CallRequestTimelineLine(line));

            foreach (var other in dto.OtherRequestsOfDay)
                OtherRequests.Add(DescribeOther(other));
        }

        OnPropertyChanged(string.Empty);
    }

    #region Contact

    public bool HasDetail => _dto is not null;

    public bool HasError => _failed;

    public string ErrorText => L["CallRequestDetailError"];

    public string? DriverPhone => string.IsNullOrWhiteSpace(_dto?.DriverPhone) ? null : _dto!.DriverPhone!.Trim();

    public bool HasPhone => DriverPhone is not null;

    public string PhoneText => DriverPhone ?? L["CallRequestNoPhone"];

    public string VehicleText => _dto?.VehicleName ?? string.Empty;

    public bool HasVehicle => !string.IsNullOrWhiteSpace(_dto?.VehicleName);

    #endregion

    #region The driver's day

    private CallRequestRouteContextDto? Route => _dto?.Route;

    public bool HasRouteContext => Route is not null;

    public string PullOutText =>
        Route is null ? string.Empty
        : Route.PulledIn ? L["CallRequestPulledIn"]
        : Route.PulledOut ? string.Format(L["CallRequestPulledOutAt"], CallRequestText.WallClock(Route.PulledOutAt))
        : L["CallRequestNotPulledOut"];

    public string ProgressText =>
        Route is null ? string.Empty : string.Format(L["CallRequestProgress"], Route.StopsDone, Route.StopsTotal);

    public string NextStopText =>
        Route?.Next is { } next ? DescribeStop(next) : L["CallRequestNoNextStop"];

    public bool HasNextStopLateness => Route?.Next?.MinutesLate is not null;

    public string NextStopLatenessText
    {
        get
        {
            var minutes = Route?.Next?.MinutesLate;

            if (minutes is null)
                return string.Empty;

            return minutes.Value > 0
                ? string.Format(L["CallRequestLate"], minutes.Value)
                : minutes.Value < 0
                    ? string.Format(L["CallRequestEarly"], -minutes.Value)
                    : L["CallRequestOnTime"];
        }
    }

    public Brush NextStopLatenessBrush => Route?.Next?.MinutesLate > 0 ? LateBrush : OnTimeBrush;

    public bool HasLastPerformed => Route?.LastPerformed is not null;

    public string LastPerformedText =>
        Route?.LastPerformed is { } last
            ? string.Format(L["CallRequestLastPerformed"], DescribeStop(last), CallRequestText.WallClock(last.PerformedAt))
            : string.Empty;

    public bool HasAtRequest => Route?.AtRequest is not null;

    public string AtRequestText =>
        Route?.AtRequest is { } stop
            ? string.Format(L["CallRequestAtRequest"], DescribeStop(stop))
            : string.Empty;

    public string LastPositionText
    {
        get
        {
            var position = _dto?.LastPosition;

            if (position is null)
                return L["CallRequestNoPosition"];

            var seen = string.Format(L["CallRequestLastSeen"], CallRequestText.Ago(position.AtUtc));

            return string.IsNullOrWhiteSpace(position.Address) ? seen : $"{seen} · {position.Address}";
        }
    }

    #endregion

    #region Closing note

    /// <summary>⚠️ Written by a dispatcher and may name a patient. Shown here only; never exported or logged.</summary>
    public string? NoteText => _dto?.ResolutionNote;

    public bool HasNote => !string.IsNullOrWhiteSpace(NoteText);

    #endregion

    public bool HasOtherRequests => OtherRequests.Count > 0;

    private static string DescribeStop(CallRequestStopDto stop)
    {
        var kind = CallRequestText.StopKind(stop.Kind);

        var trip = stop.TripId.HasValue
            ? $" · {L["CallRequestTrip"]} {stop.TripId}"
            : string.Empty;

        var scheduled = stop.ScheduledTime.HasValue
            ? $" · {CallRequestText.WallClock(stop.ScheduledTime)}"
            : string.Empty;

        var eta = stop.Eta.HasValue && !stop.Performed
            ? $" · {string.Format(L["CallRequestEta"], CallRequestText.WallClock(stop.Eta))}"
            : string.Empty;

        return $"{kind}{trip}{scheduled}{eta}";
    }

    private static string DescribeOther(CallRequestSummaryDto other)
    {
        var closing = string.IsNullOrWhiteSpace(other.ReasonCode)
            ? string.Empty
            : $" · {CallRequestText.Reason(other.ReasonCode)}";

        return $"{CallRequestText.ShortTime(other.RequestedAtUtc)} · {CallRequestItemViewModel.StatusTextFor(other)}{closing}";
    }

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

/// <summary>One line of a request's history, as the dispatcher reads it.</summary>
public sealed class CallRequestTimelineLine
{
    public CallRequestTimelineLine(CallRequestTimelineItemDto item)
    {
        TimeText = CallRequestText.ExactTime(item.AtUtc);

        var what = CallRequestText.Change(item.Type);

        var detail = item.Type switch
        {
            CallRequestChanges.Resolved => CallRequestText.Reason(item.Detail),
            CallRequestChanges.TakenOver when !string.IsNullOrWhiteSpace(item.Detail) =>
                string.Format(LocalizationService.Instance["CallRequestTakenOverFrom"], item.Detail),
            _ => null
        };

        Text = string.Join(" · ", new[] { what, item.ByName, detail }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public string TimeText { get; }

    public string Text { get; }
}
