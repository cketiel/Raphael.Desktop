using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services.CallRequests
{
    public interface ICallRequestApiClient
    {
        Task<List<CallRequestSummaryDto>> GetQueueAsync();

        Task<CallRequestDetailDto?> GetDetailAsync(int id);

        Task<CallRequestChangeResult> ClaimAsync(int id);

        Task<CallRequestChangeResult> TakeOverAsync(int id);

        Task<CallRequestChangeResult> ReleaseAsync(int id);

        Task<CallRequestChangeResult> NoAnswerAsync(int id);

        Task<CallRequestChangeResult> ResolveAsync(int id, string reasonCode, string? note);

        Task<CallRequestChangeResult> ReopenAsync(int id);
    }

    /// <summary>
    /// What happened to a change. On a conflict <see cref="Request"/> is how the request stands
    /// now, so the screen can say who got there first instead of just "failed".
    /// </summary>
    public sealed class CallRequestChangeResult
    {
        public bool Succeeded { get; init; }

        public bool IsConflict { get; init; }

        public CallRequestSummaryDto? Request { get; init; }

        public string? Message { get; init; }
    }

    public sealed class CallRequestApiClient : ICallRequestApiClient
    {
        private const string Endpoint = "call-requests";

        private readonly HttpClient _http = ApiClientFactory.Create();

        public async Task<List<CallRequestSummaryDto>> GetQueueAsync()
        {
            var response = await _http.GetAsync(Endpoint);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<CallRequestSummaryDto>>() ?? [];
        }

        public async Task<CallRequestDetailDto?> GetDetailAsync(int id)
        {
            var response = await _http.GetAsync($"{Endpoint}/{id}");

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<CallRequestDetailDto>();
        }

        public Task<CallRequestChangeResult> ClaimAsync(int id) => PostAsync($"{Endpoint}/{id}/claim");

        public Task<CallRequestChangeResult> TakeOverAsync(int id) => PostAsync($"{Endpoint}/{id}/take-over");

        public Task<CallRequestChangeResult> ReleaseAsync(int id) => PostAsync($"{Endpoint}/{id}/release");

        public Task<CallRequestChangeResult> NoAnswerAsync(int id) => PostAsync($"{Endpoint}/{id}/no-answer");

        public Task<CallRequestChangeResult> ResolveAsync(int id, string reasonCode, string? note) =>
            PostAsync(
                $"{Endpoint}/{id}/resolve",
                new ResolveCallRequestDto { ReasonCode = reasonCode, Note = note });

        public Task<CallRequestChangeResult> ReopenAsync(int id) => PostAsync($"{Endpoint}/{id}/reopen");

        private async Task<CallRequestChangeResult> PostAsync(string path, object? body = null)
        {
            var response = body is null
                ? await _http.PostAsync(path, null)
                : await _http.PostAsJsonAsync(path, body);

            if (response.IsSuccessStatusCode)
            {
                return new CallRequestChangeResult
                {
                    Succeeded = true,
                    Request = await response.Content.ReadFromJsonAsync<CallRequestSummaryDto>()
                };
            }

            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
            {
                var problem = await ReadProblemAsync(response);

                return new CallRequestChangeResult
                {
                    IsConflict = response.StatusCode == HttpStatusCode.Conflict,
                    Request = problem?.Current,
                    Message = problem?.Message
                };
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new CallRequestChangeResult { Message = LocalizationService.Instance["CallRequestNotFound"] };

            response.EnsureSuccessStatusCode();

            return new CallRequestChangeResult();
        }

        private static async Task<CallRequestConflictDto?> ReadProblemAsync(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadFromJsonAsync<CallRequestConflictDto>();
            }
            catch
            {
                return null;
            }
        }
    }
}
