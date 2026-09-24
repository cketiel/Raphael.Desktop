using System.Windows;
using System.Windows.Input;
using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.CallRequests;

namespace Raphael.Desktop.ViewModels.CallRequests;

/// <summary>
/// The call a dispatcher is handling, shown on top of the driver's route.
/// </summary>
/// <remarks>
/// The whole point of attending from here: the phone number, how long the driver has waited, the
/// stop they are on and the buttons to close the case sit above the route, its trips and the live
/// vehicle, so nobody has to switch screens while the driver is on the line.
/// </remarks>
public sealed class CallCardViewModel : BaseViewModel, IDisposable
{
    private readonly CallRequestActions _actions;

    private readonly CallRequestBoard _board;

    private bool _routeUnavailable;

    private bool _disposed;

    public CallCardViewModel(CallRequestActions actions, CallRequestSummaryDto request)
    {
        _actions = actions;
        _board = actions.Board;

        Item = new CallRequestItemViewModel(_board.Get(request.Id) ?? request);

        Detail = new CallRequestDetailViewModel(_board.Api);

        Resolve = new CallResolveViewModel();
        Resolve.Confirmed += async (code, note) => await _actions.ResolveAsync(Item.Dto, code, note);

        CopyPhoneCommand = new RelayCommandObject(
            _ =>
            {
                if (Detail.DriverPhone is { } phone)
                    Clipboard.SetText(phone);
            },
            _ => Detail.HasPhone);

        AttendCommand = new AsyncRelayCommand(_ => _actions.AttendAsync(Item.Dto), _ => Item.IsWaiting);

        NoAnswerCommand = new AsyncRelayCommand(_ => _actions.NoAnswerAsync(Item.Dto), _ => Item.IsMine);

        ReleaseCommand = new AsyncRelayCommand(
            async _ =>
            {
                if (await _actions.ReleaseAsync(Item.Dto))
                    CloseRequested?.Invoke(this, EventArgs.Empty);
            },
            _ => Item.IsMine);

        StartResolveCommand = new RelayCommandObject(_ => Resolve.Begin(), _ => Item.IsMine || Item.IsWaiting);

        TakeOverCommand = new AsyncRelayCommand(_ => _actions.TakeOverAsync(Item.Dto), _ => Item.IsHeldByOther);

        AttendNextCommand = new AsyncRelayCommand(_ => _actions.AttendNextAsync(), _ => _board.WaitingCount > 0);

        OpenInQueueCommand = new RelayCommandObject(_ => OpenInQueueRequested?.Invoke(this, Item.Id));

        CloseCommand = new RelayCommandObject(_ => CloseRequested?.Invoke(this, EventArgs.Empty));

        _board.Changed += OnBoardChanged;
        _board.Tick += OnTick;

        _ = Detail.LoadAsync(request.Id);
    }

    private static LocalizationService L => LocalizationService.Instance;

    /// <summary>The dispatcher closed the card, released the case, or the tab is going.</summary>
    public event EventHandler? CloseRequested;

    public event EventHandler<int>? OpenInQueueRequested;

    public CallRequestItemViewModel Item { get; }

    public CallRequestDetailViewModel Detail { get; }

    public CallResolveViewModel Resolve { get; }

    public int RequestId => Item.Id;

    public ICommand CopyPhoneCommand { get; }

    public ICommand AttendCommand { get; }

    public ICommand NoAnswerCommand { get; }

    public ICommand ReleaseCommand { get; }

    public ICommand StartResolveCommand { get; }

    public ICommand TakeOverCommand { get; }

    public ICommand AttendNextCommand { get; }

    public ICommand OpenInQueueCommand { get; }

    public ICommand CloseCommand { get; }

    public int WaitingCount => _board.WaitingCount;

    public string AttendNextText => string.Format(L["CallRequestAttendNext"], WaitingCount);

    public string ClosedText =>
        Item.HasReason ? $"{Item.StatusText} · {Item.ReasonText}" : Item.StatusText;

    /// <summary>Somebody else has the case now: the card stays, but only to say so.</summary>
    public string HeldByOtherText =>
        string.Format(L["CallRequestHeldBy"], Item.Dto.ClaimedByName, CallRequestText.ShortTime(Item.Dto.ClaimedAtUtc));

    public bool RouteUnavailable
    {
        get => _routeUnavailable;
        private set => SetProperty(ref _routeUnavailable, value);
    }

    /// <summary>The Schedule tab could not show this driver's route: it is outside its dates or suspended.</summary>
    public void MarkRouteUnavailable() => RouteUnavailable = true;

    private void OnBoardChanged(object? sender, EventArgs e)
    {
        var fresh = _board.Get(Item.Id);

        if (fresh is not null && fresh.Revision != Item.Dto.Revision)
        {
            Item.Update(fresh);
            _ = Detail.LoadAsync(Item.Id);
        }

        OnPropertyChanged(nameof(WaitingCount));
        OnPropertyChanged(nameof(AttendNextText));
        OnPropertyChanged(nameof(ClosedText));
        OnPropertyChanged(nameof(HeldByOtherText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Item.Tick();

        // The route moves while the driver talks: the next stop's ETA, the last one performed.
        if (Item.IsOpen)
            _ = Detail.LoadAsync(Item.Id);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _board.Changed -= OnBoardChanged;
        _board.Tick -= OnTick;
    }
}
