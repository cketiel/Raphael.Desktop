using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.CallRequests;

namespace Raphael.Desktop.ViewModels.CallRequests;

/// <summary>
/// The "Calls" tab of the Notification Center: the drivers' call-back queue.
/// </summary>
/// <remarks>
/// Waiting requests first, in order of arrival — the office answers them in that order — then the
/// ones being handled, then what closed today, folded away. A view over <see cref="CallRequestBoard"/>,
/// so every dispatcher's screen shows the same thing at the same moment.
/// </remarks>
public sealed class CallRequestsPanelViewModel : BaseViewModel
{
    private readonly CallRequestActions _actions;

    private readonly CallRequestBoard _board;

    private readonly Dictionary<int, CallRequestItemViewModel> _items = new();

    private CallRequestItemViewModel? _selected;

    private long _selectedRevision = -1;

    private bool _showClosed;

    public CallRequestsPanelViewModel(CallRequestActions actions)
    {
        _actions = actions;
        _board = actions.Board;

        Detail = new CallRequestDetailViewModel(_board.Api);

        Resolve = new CallResolveViewModel();
        Resolve.Confirmed += async (code, note) =>
        {
            if (Selected is { } item)
                await _actions.ResolveAsync(item.Dto, code, note);
        };

        AttendCommand = new AsyncRelayCommand(
            p => ItemFrom(p) is { } item ? _actions.AttendAsync(item.Dto) : Task.CompletedTask,
            p => ItemFrom(p)?.IsWaiting == true);

        TakeOverCommand = new AsyncRelayCommand(
            p => ItemFrom(p) is { } item ? _actions.TakeOverAsync(item.Dto) : Task.CompletedTask,
            p => ItemFrom(p)?.IsHeldByOther == true);

        ReleaseCommand = new AsyncRelayCommand(
            p => ItemFrom(p) is { } item ? _actions.ReleaseAsync(item.Dto) : Task.CompletedTask,
            p => ItemFrom(p)?.IsMine == true);

        NoAnswerCommand = new AsyncRelayCommand(
            p => ItemFrom(p) is { } item ? _actions.NoAnswerAsync(item.Dto) : Task.CompletedTask,
            p => ItemFrom(p)?.IsMine == true);

        StartResolveCommand = new RelayCommandObject(
            p =>
            {
                if (ItemFrom(p) is not { } item)
                    return;

                Selected = item;
                Resolve.Begin();
            },
            p => ItemFrom(p) is { } item && (item.IsWaiting || item.IsMine));

        ReopenCommand = new AsyncRelayCommand(
            p => ItemFrom(p) is { } item ? _actions.ReopenAsync(item.Dto) : Task.CompletedTask,
            p => ItemFrom(p)?.CanReopen == true);

        OpenRouteCommand = new RelayCommandObject(
            p =>
            {
                if (ItemFrom(p) is { } item)
                    _actions.OpenRoute(item.Dto);
            },
            p => ItemFrom(p)?.HasRoute == true);

        CopyPhoneCommand = new RelayCommandObject(
            _ =>
            {
                if (Detail.DriverPhone is { } phone)
                    Clipboard.SetText(phone);
            },
            _ => Detail.HasPhone);

        AttendNextCommand = new AsyncRelayCommand(
            _ => _actions.AttendNextAsync(),
            _ => _board.WaitingCount > 0);

        RefreshCommand = new AsyncRelayCommand(_ => _board.ReloadAsync());

        ExportCommand = new RelayCommandObject(_ => Export());

        _board.Changed += (_, _) => Sync();
        _board.Tick += (_, _) => Tick();

        Sync();
    }

    private static LocalizationService L => LocalizationService.Instance;

    public ObservableCollection<CallRequestItemViewModel> Open { get; } = [];

    public ObservableCollection<CallRequestItemViewModel> Closed { get; } = [];

    public CallRequestDetailViewModel Detail { get; }

    public CallResolveViewModel Resolve { get; }

    public ICommand AttendCommand { get; }

    public ICommand TakeOverCommand { get; }

    public ICommand ReleaseCommand { get; }

    public ICommand NoAnswerCommand { get; }

    public ICommand StartResolveCommand { get; }

    public ICommand ReopenCommand { get; }

    public ICommand OpenRouteCommand { get; }

    public ICommand CopyPhoneCommand { get; }

    public ICommand AttendNextCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ExportCommand { get; }

    public CallRequestItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;

            Resolve.Close();

            _selectedRevision = value?.Dto.Revision ?? -1;

            if (value is null)
                Detail.Clear();
            else
                _ = Detail.LoadAsync(value.Id);

            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected is not null;

    public bool ShowClosed
    {
        get => _showClosed;
        set => SetProperty(ref _showClosed, value);
    }

    public int WaitingCount => _board.WaitingCount;

    public bool HasWaiting => WaitingCount > 0;

    public bool IsEmpty => Open.Count == 0;

    #region The day in numbers

    private IEnumerable<CallRequestSummaryDto> Today =>
        _board.All.Where(r => r.OperatingDate.Date == DateTime.Today);

    public string TodaySummaryText
    {
        get
        {
            var today = Today.ToList();

            var attended = today
                .Where(r => r.ClaimedAtUtc.HasValue)
                .Select(r => r.ClaimedAtUtc!.Value - r.RequestedAtUtc)
                .ToList();

            var handled = today
                .Where(r => r.Status == CallRequestStatuses.Resolved && r.ClaimedAtUtc.HasValue && r.ClosedAtUtc.HasValue)
                .Select(r => r.ClosedAtUtc!.Value - r.ClaimedAtUtc!.Value)
                .ToList();

            var topReason = today
                .Where(r => !string.IsNullOrWhiteSpace(r.ReasonCode))
                .GroupBy(r => r.ReasonCode)
                .OrderByDescending(g => g.Count())
                .Select(g => CallRequestText.Reason(g.Key))
                .FirstOrDefault();

            var parts = new List<string>
            {
                string.Format(L["CallRequestSummaryCount"], today.Count)
            };

            if (attended.Count > 0)
                parts.Add(string.Format(L["CallRequestSummaryWait"], CallRequestText.Duration(Average(attended))));

            if (handled.Count > 0)
                parts.Add(string.Format(L["CallRequestSummaryHandle"], CallRequestText.Duration(Average(handled))));

            if (topReason is not null)
                parts.Add(string.Format(L["CallRequestSummaryTopReason"], topReason));

            return string.Join("  ·  ", parts);
        }
    }

    private static TimeSpan Average(List<TimeSpan> spans) =>
        TimeSpan.FromTicks((long)spans.Average(span => span.Ticks));

    #endregion

    /// <summary>Selects one request, e.g. from an alert or the header counter.</summary>
    public void Show(int? callRequestId)
    {
        if (callRequestId is not int id || !_items.TryGetValue(id, out var item))
            return;

        if (item.IsClosed)
            ShowClosed = true;

        Selected = item;
    }

    private void Sync()
    {
        var seen = new HashSet<int>();

        foreach (var request in _board.All)
        {
            seen.Add(request.Id);

            if (_items.TryGetValue(request.Id, out var item))
            {
                if (item.Dto.Revision != request.Revision || !ReferenceEquals(item.Dto, request))
                    item.Update(request);
            }
            else
            {
                _items[request.Id] = new CallRequestItemViewModel(request);
            }
        }

        foreach (var gone in _items.Keys.Where(id => !seen.Contains(id)).ToList())
            _items.Remove(gone);

        var open = _items.Values
            .Where(i => i.IsOpen)
            .OrderBy(i => i.IsWaiting ? 0 : 1)
            .ThenBy(i => i.IsWaiting ? i.Dto.QueuedAtUtc : i.Dto.ClaimedAtUtc ?? i.Dto.QueuedAtUtc)
            .ToList();

        var closed = _items.Values
            .Where(i => i.IsClosed)
            .OrderByDescending(i => i.Dto.ClosedAtUtc ?? i.Dto.QueuedAtUtc)
            .ToList();

        Reconcile(Open, open);
        Reconcile(Closed, closed);

        if (Selected is not null && !_items.ContainsKey(Selected.Id))
        {
            Selected = null;
        }
        else if (Selected is not null && Selected.Dto.Revision != _selectedRevision)
        {
            // What is open in the detail changed under the dispatcher: its timeline and its route did too.
            _selectedRevision = Selected.Dto.Revision;
            _ = Detail.LoadAsync(Selected.Id);
        }

        OnPropertyChanged(nameof(WaitingCount));
        OnPropertyChanged(nameof(HasWaiting));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TodaySummaryText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Tick()
    {
        foreach (var item in _items.Values)
            item.Tick();

        OnPropertyChanged(nameof(TodaySummaryText));
    }

    /// <summary>Moves rows into place instead of clearing the list, so the selection and the scroll survive.</summary>
    private static void Reconcile(
        ObservableCollection<CallRequestItemViewModel> target,
        List<CallRequestItemViewModel> desired)
    {
        var keep = new HashSet<CallRequestItemViewModel>(desired);

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(target[i]))
                target.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var current = target.IndexOf(desired[i]);

            if (current < 0)
                target.Insert(i, desired[i]);
            else if (current != i)
                target.Move(current, i);
        }
    }

    private CallRequestItemViewModel? ItemFrom(object? parameter) =>
        parameter as CallRequestItemViewModel ?? Selected;

    /// <summary>
    /// The day's requests to Excel. ⚠️ Without the closing note: it may name a patient, and an
    /// exported file leaves every control this system has.
    /// </summary>
    private void Export()
    {
        var rows = Today.OrderBy(r => r.RequestedAtUtc).ToList();

        if (rows.Count == 0)
        {
            UiError.Show(L["CallRequestExportEmpty"], L["CallRequestsTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"call-requests-{DateTime.Today:yyyy-MM-dd}.xlsx"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Calls");

            var headers = new[]
            {
                L["CallRequestColDriver"], L["CallRequestColRoute"], L["CallRequestColRequested"],
                L["CallRequestColReminders"], L["CallRequestColTakenBy"], L["CallRequestColTakenAt"],
                L["CallRequestColAttempts"], L["CallRequestColStatus"], L["CallRequestColClosedAt"],
                L["CallRequestColReason"], L["CallRequestColWaitMinutes"], L["CallRequestColHandleMinutes"]
            };

            for (var c = 0; c < headers.Length; c++)
                sheet.Cell(1, c + 1).Value = headers[c];

            var row = 2;

            foreach (var r in rows)
            {
                sheet.Cell(row, 1).Value = r.DriverName;
                sheet.Cell(row, 2).Value = r.RouteName ?? string.Empty;
                sheet.Cell(row, 3).Value = CallRequestText.Local(r.RequestedAtUtc);
                sheet.Cell(row, 4).Value = r.ReminderCount;
                sheet.Cell(row, 5).Value = r.ClaimedByName ?? string.Empty;

                if (r.ClaimedAtUtc.HasValue)
                    sheet.Cell(row, 6).Value = CallRequestText.Local(r.ClaimedAtUtc.Value);

                sheet.Cell(row, 7).Value = r.CallAttempts;
                sheet.Cell(row, 8).Value = CallRequestItemViewModel.StatusTextFor(r);

                if (r.ClosedAtUtc.HasValue)
                    sheet.Cell(row, 9).Value = CallRequestText.Local(r.ClosedAtUtc.Value);

                sheet.Cell(row, 10).Value = CallRequestText.Reason(r.ReasonCode);

                if (r.ClaimedAtUtc.HasValue)
                    sheet.Cell(row, 11).Value = Math.Round((r.ClaimedAtUtc.Value - r.RequestedAtUtc).TotalMinutes, 1);

                if (r.ClaimedAtUtc.HasValue && r.ClosedAtUtc.HasValue && r.Status == CallRequestStatuses.Resolved)
                    sheet.Cell(row, 12).Value = Math.Round((r.ClosedAtUtc.Value - r.ClaimedAtUtc.Value).TotalMinutes, 1);

                row++;
            }

            sheet.Columns().AdjustToContents();
            workbook.SaveAs(dialog.FileName);
        }
        catch (Exception ex)
        {
            FileLogger.Log($"Call requests: export failed. {ex.Message}");
            UiError.Show(L["CallRequestExportFailed"], L["CallRequestsTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
