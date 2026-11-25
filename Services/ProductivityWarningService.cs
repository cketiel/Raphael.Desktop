using Raphael.Desktop.DTOs;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace Raphael.Desktop.Services
{
    /// <summary>RE-092: the report's warnings, their limits, and a vehicle's odometer.</summary>
    public interface IProductivityWarningService
    {
        Task<List<ProductivityDayWarningsDto>?> ListAsync(DateTime start, DateTime end);
        Task<ProductivityDayWarningsDto?> DetailAsync(int vehicleRouteId, DateTime date);
        Task<List<WarningLimitDto>?> LimitsAsync();
        Task<List<WarningLimitDto>> SaveLimitsAsync(List<WarningLimitUpdateDto> changes);
        Task<List<WarningPreviewDto>> PreviewAsync(List<WarningLimitUpdateDto> changes, CancellationToken ct);
        Task<PayRuleDto?> PayRuleAsync();
        Task<PayRuleDto?> SavePayRuleAsync(int inspectionMinutes);
        Task<VehicleOdometerHistoryDto?> HistoryAsync(int vehicleId);
        Task<HttpStatusCode> CorrectAsync(int scheduleId, OdometerCorrectionRequestDto request);
    }

    public class ProductivityWarningService : IProductivityWarningService
    {
        private readonly HttpClient _http = ApiClientFactory.Create();

        /// <summary>Null when the server has no warnings to give (older than RE-092): the report keeps its own.</summary>
        public async Task<List<ProductivityDayWarningsDto>?> ListAsync(DateTime start, DateTime end) =>
            await GetAsync<List<ProductivityDayWarningsDto>>($"reports/productivity/warnings?startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}");

        public async Task<ProductivityDayWarningsDto?> DetailAsync(int vehicleRouteId, DateTime date) =>
            await GetAsync<ProductivityDayWarningsDto>($"reports/productivity/warnings/{vehicleRouteId}/{date:yyyy-MM-dd}");

        public async Task<List<WarningLimitDto>?> LimitsAsync() =>
            await GetAsync<List<WarningLimitDto>>("reports/productivity/warning-limits");

        public async Task<List<WarningLimitDto>> SaveLimitsAsync(List<WarningLimitUpdateDto> changes)
        {
            var response = await _http.PutAsJsonAsync("reports/productivity/warning-limits", changes);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<WarningLimitDto>>() ?? new();
        }

        public async Task<List<WarningPreviewDto>> PreviewAsync(List<WarningLimitUpdateDto> changes, CancellationToken ct)
        {
            var response = await _http.PostAsJsonAsync("reports/productivity/warning-limits/preview", changes, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<WarningPreviewDto>>(cancellationToken: ct) ?? new();
        }

        public async Task<PayRuleDto?> PayRuleAsync() => await GetAsync<PayRuleDto>("reports/productivity/pay-rule");

        public async Task<PayRuleDto?> SavePayRuleAsync(int inspectionMinutes)
        {
            var response = await _http.PutAsync($"reports/productivity/pay-rule/inspection-minutes/{inspectionMinutes}", null);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PayRuleDto>();
        }

        public async Task<VehicleOdometerHistoryDto?> HistoryAsync(int vehicleId) =>
            await GetAsync<VehicleOdometerHistoryDto>($"odometer/vehicles/{vehicleId}?days=60");

        /// <summary>The status of the answer: 204 saved, 409 changed meanwhile.</summary>
        public async Task<HttpStatusCode> CorrectAsync(int scheduleId, OdometerCorrectionRequestDto request) =>
            (await _http.PostAsJsonAsync($"odometer/schedules/{scheduleId}/correction", request)).StatusCode;

        private async Task<T?> GetAsync<T>(string uri) where T : class
        {
            var response = await _http.GetAsync(uri);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>();
        }
    }
}
