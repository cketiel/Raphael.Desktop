using System.Net.Http;
using System.Net.Http.Json;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services
{
    /// <summary>A trip's charge and its additional charges, and applying an extra to a day's trips (RE-074).</summary>
    public interface ITripChargesService
    {
        Task<TripChargesDto?> GetAsync(int tripId);
        Task<TripChargesDto?> SetExtrasAsync(int tripId, List<TripChargeExtraDto> extras);
        Task<ExtraBulkPreviewDto?> PreviewDayAsync(int ruleId, DateTime date, bool remove);
        Task<ExtraBulkPreviewDto?> ApplyDayAsync(int ruleId, DateTime date, bool remove);
        Task<ExtraBulkPreviewDto?> UndoDayAsync(int ruleId, DateTime date, DateTime batchAtUtc);
    }

    public sealed class TripChargesService : ITripChargesService
    {
        private readonly HttpClient _http = ApiClientFactory.Create();

        public async Task<TripChargesDto?> GetAsync(int tripId) =>
            await _http.GetFromJsonAsync<TripChargesDto>($"trips/{tripId}/charges");

        public async Task<TripChargesDto?> SetExtrasAsync(int tripId, List<TripChargeExtraDto> extras)
        {
            var response = await _http.PutAsJsonAsync($"trips/{tripId}/charges/extras", extras);
            await EnsureAsync(response);
            return await response.Content.ReadFromJsonAsync<TripChargesDto>();
        }

        public async Task<ExtraBulkPreviewDto?> PreviewDayAsync(int ruleId, DateTime date, bool remove)
        {
            var response = await _http.GetAsync($"FundingSourceBillingItem/{ruleId}/day/preview?date={date:yyyy-MM-dd}&remove={remove}");
            await EnsureAsync(response);
            return await response.Content.ReadFromJsonAsync<ExtraBulkPreviewDto>();
        }

        public async Task<ExtraBulkPreviewDto?> ApplyDayAsync(int ruleId, DateTime date, bool remove)
        {
            var response = await _http.PostAsync($"FundingSourceBillingItem/{ruleId}/day?date={date:yyyy-MM-dd}&remove={remove}", null);
            await EnsureAsync(response);
            return await response.Content.ReadFromJsonAsync<ExtraBulkPreviewDto>();
        }

        public async Task<ExtraBulkPreviewDto?> UndoDayAsync(int ruleId, DateTime date, DateTime batchAtUtc)
        {
            // The instant exactly as the server sent it: it identifies the application to undo.
            var at = Uri.EscapeDataString(batchAtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture));
            var response = await _http.PostAsync($"FundingSourceBillingItem/{ruleId}/day/undo?date={date:yyyy-MM-dd}&batchAtUtc={at}", null);
            await EnsureAsync(response);
            return await response.Content.ReadFromJsonAsync<ExtraBulkPreviewDto>();
        }

        private static async Task EnsureAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            string? message = null;
            try { message = (await response.Content.ReadFromJsonAsync<Problem>())?.Message; } catch { }
            throw new BillingRuleApiException(message ?? $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        private sealed class Problem { public string? Message { get; set; } }
    }
}
