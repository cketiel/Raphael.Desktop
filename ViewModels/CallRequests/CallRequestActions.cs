using System.Windows;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.CallRequests;

namespace Raphael.Desktop.ViewModels.CallRequests;

/// <summary>
/// What a dispatcher can do to a request, the same from the queue and from the call card.
/// </summary>
/// <remarks>
/// Every answer the server gives is applied to the board straight away, success or conflict: a
/// dispatcher who lost a race sees who won in the same moment they are told.
/// </remarks>
public sealed class CallRequestActions
{
    private readonly CallRequestBoard _board;

    private readonly Action<CallRequestSummaryDto> _openRoute;

    public CallRequestActions(CallRequestBoard board, Action<CallRequestSummaryDto> openRoute)
    {
        _board = board;
        _openRoute = openRoute;
    }

    private static LocalizationService L => LocalizationService.Instance;

    public CallRequestBoard Board => _board;

    /// <summary>Takes the request and opens the driver's route with the call card on top.</summary>
    public async Task<bool> AttendAsync(CallRequestSummaryDto request)
    {
        var result = await RunAsync(() => _board.Api.ClaimAsync(request.Id));

        if (result is null)
            return false;

        if (result.Succeeded)
        {
            _board.Apply(result.Request);
            OpenRoute(result.Request ?? request);
            return true;
        }

        // Someone got there first. Say who, and offer to take over rather than just refusing.
        if (result.IsConflict &&
            result.Request is { Status: CallRequestStatuses.InProgress } current &&
            current.ClaimedByUserId != CallRequestBoard.CurrentUserId)
        {
            _board.Apply(current);

            return await TakeOverAsync(current);
        }

        Refused(result);
        return false;
    }

    public async Task<bool> TakeOverAsync(CallRequestSummaryDto request)
    {
        if (request.Status == CallRequestStatuses.InProgress &&
            request.ClaimedByUserId != CallRequestBoard.CurrentUserId)
        {
            var question = string.Format(
                L["CallRequestTakeOverQuestion"],
                request.ClaimedByName,
                CallRequestText.Ago(request.ClaimedAtUtc));

            if (UiError.Show(question, L["CallRequestsTitle"], MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        var result = await RunAsync(() => _board.Api.TakeOverAsync(request.Id));

        if (result?.Succeeded == true)
        {
            _board.Apply(result.Request);
            OpenRoute(result.Request ?? request);
            return true;
        }

        Refused(result);
        return false;
    }

    public Task<bool> ReleaseAsync(CallRequestSummaryDto request) =>
        SimpleAsync(() => _board.Api.ReleaseAsync(request.Id));

    public Task<bool> NoAnswerAsync(CallRequestSummaryDto request) =>
        SimpleAsync(() => _board.Api.NoAnswerAsync(request.Id));

    public Task<bool> ResolveAsync(CallRequestSummaryDto request, string reasonCode, string? note) =>
        SimpleAsync(() => _board.Api.ResolveAsync(request.Id, reasonCode, note));

    public Task<bool> ReopenAsync(CallRequestSummaryDto request) =>
        SimpleAsync(() => _board.Api.ReopenAsync(request.Id));

    /// <summary>The oldest request still waiting: the office works in order of arrival.</summary>
    public async Task<bool> AttendNextAsync()
    {
        var next = _board.OldestWaiting;

        if (next is null)
        {
            UiError.Show(L["CallRequestQueueEmpty"], L["CallRequestsTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return await AttendAsync(next);
    }

    public void OpenRoute(CallRequestSummaryDto request)
    {
        if (request.VehicleRouteId is null)
        {
            UiError.Show(L["CallRequestNoRouteToOpen"], L["CallRequestsTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _openRoute(request);
    }

    private async Task<bool> SimpleAsync(Func<Task<CallRequestChangeResult>> call)
    {
        var result = await RunAsync(call);

        if (result?.Succeeded == true)
        {
            _board.Apply(result.Request);
            return true;
        }

        Refused(result);
        return false;
    }

    private static async Task<CallRequestChangeResult?> RunAsync(Func<Task<CallRequestChangeResult>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            FileLogger.Log($"Call requests: a change failed. {ex.Message}");
            UiError.Show(L["CallRequestNetworkError"], L["CallRequestsTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private void Refused(CallRequestChangeResult? result)
    {
        if (result is null)
            return;

        _board.Apply(result.Request);

        UiError.Show(Explain(result), L["CallRequestsTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Why the server said no, in the dispatcher's language, from how the request stands now.</summary>
    private static string Explain(CallRequestChangeResult result)
    {
        var current = result.Request;

        if (current is { Status: CallRequestStatuses.InProgress } &&
            current.ClaimedByUserId != CallRequestBoard.CurrentUserId)
        {
            return string.Format(
                L["CallRequestHeldBy"],
                current.ClaimedByName,
                CallRequestText.ShortTime(current.ClaimedAtUtc));
        }

        if (current is { Status: CallRequestStatuses.Resolved or CallRequestStatuses.Cancelled or CallRequestStatuses.Expired })
            return L["CallRequestAlreadyClosed"];

        return string.IsNullOrWhiteSpace(result.Message)
            ? L["CallRequestChangeRefused"]
            : result.Message!;
    }
}
