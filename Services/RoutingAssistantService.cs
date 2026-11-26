#nullable enable
using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>RE-098: <c>api/routing-assistant</c>. Null answers: no longer open, or a server without it.</summary>
    public class RoutingAssistantService
    {
        private readonly HttpClient _http = ApiClientFactory.Create();

        public Task<RoutingSuggestionsDto?> LinesAsync(int tripId, CancellationToken ct) =>
            PostAsync<RoutingSuggestionsDto>("lines", new LineSuggestionsRequestDto { TripId = tripId }, ct);

        public Task<RoutingSuggestionsDto?> TripsAsync(int routeId, DateTime date, CancellationToken ct) =>
            PostAsync<RoutingSuggestionsDto>("trips", new TripSuggestionsRequestDto { VehicleRouteId = routeId, Date = date.Date }, ct);

        public Task<RoutingSuggestionDto?> DetailAsync(RoutingSuggestionDto s, CancellationToken ct) =>
            PostAsync<RoutingSuggestionDto>("detail", new SuggestionDetailRequestDto
            {
                TripId = s.TripId, VehicleRouteId = s.VehicleRouteId,
                PickupAfterScheduleId = s.PickupAfterScheduleId, DropoffAfterScheduleId = s.DropoffAfterScheduleId,
            }, ct);

        /// <summary>The day's plan; null with a server that does not have it.</summary>
        public async Task<DayPlanDto?> PlanAsync(DateTime date, CancellationToken ct)
        {
            var response = await _http.GetAsync($"routing-assistant/plan?date={date:yyyy-MM-dd}", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<DayPlanDto>(cancellationToken: ct);
        }

        private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
        {
            var response = await _http.PostAsJsonAsync($"routing-assistant/{path}", body, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
    }

    /// <summary>RE-094: <c>api/trip-feasibility</c>. A server without it answers nothing and no icon shows.</summary>
    public class TripFeasibilityService
    {
        private readonly HttpClient _http = ApiClientFactory.Create();

        public async Task<List<TripFeasibilityDto>> ForDaysAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            var response = await _http.GetAsync($"trip-feasibility?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);
            if (!response.IsSuccessStatusCode) return new();
            return await response.Content.ReadFromJsonAsync<List<TripFeasibilityDto>>(cancellationToken: ct) ?? new();
        }

        public async Task<TripFeasibilityDto?> PreviewAsync(FeasibilityPreviewRequestDto request, CancellationToken ct)
        {
            var response = await _http.PostAsJsonAsync("trip-feasibility/preview", request, ct);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            return await response.Content.ReadFromJsonAsync<TripFeasibilityDto>(cancellationToken: ct);
        }

        public async Task<ImpossibleAppointmentsReportDto> ReportAsync(DateTime from, DateTime to, int? fundingSourceId, CancellationToken ct)
        {
            var url = $"trip-feasibility/report?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" + (fundingSourceId is int id ? $"&fundingSourceId={id}" : "");
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ImpossibleAppointmentsReportDto>(cancellationToken: ct))!;
        }

        /// <summary>RE-095: the impossible and loose trips of a file just imported; null when the server does not know.</summary>
        public async Task<ImportHoursReviewDto?> ReviewImportAsync(ImportHoursRequestDto request)
        {
            var response = await _http.PostAsJsonAsync("trip-feasibility/import-review", request);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ImportHoursReviewDto>();
        }

        public async Task<HoursAdjustResultDto> HoursAsync(IEnumerable<int> tripIds, string action)
        {
            var response = await _http.PostAsJsonAsync("trip-feasibility/hours", new HoursAdjustRequestDto { TripIds = new(tripIds), Action = action });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<HoursAdjustResultDto>())!;
        }

        /// <summary>Queues the appointments of the range that were never checked. Administrators.</summary>
        public async Task<int> CheckAsync(DateTime from, DateTime to)
        {
            var response = await _http.PostAsync($"trip-feasibility/check?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", null);
            response.EnsureSuccessStatusCode();
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("queued").GetInt32();
        }
    }
}
