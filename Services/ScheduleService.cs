using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public class ScheduleService
    {
        //private readonly string _baseUrl = "https://localhost:7123/api/"; 
        private readonly HttpClient _httpClient;
        private readonly string _endPoint = "schedules";
        private readonly TripHistoryService _historyService;

        public ScheduleService()
        {
            _httpClient = ApiClientFactory.Create();
            _historyService = new TripHistoryService();
        }

        public async Task<List<ScheduleDto>> GetSchedulesAsync(int routeId, DateTime date)
        {
            //var url = $"{_baseUrl}schedules/by-route?vehicleRouteId={routeId}&date={date:yyyy-MM-dd}";
            var url = $"{_endPoint}/by-route?vehicleRouteId={routeId}&date={date:yyyy-MM-dd}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<ScheduleDto>>();
        }

        /// <summary>
        /// The two events of one trip, or <c>null</c> when this API does not serve them yet.
        /// </summary>
        /// <remarks>
        /// ⚠️ The null is not "no events" — that comes back as an empty list. It means the
        /// endpoint is not there, which is the normal state until Raphael.Backend is
        /// deployed. Callers fall back to reading the whole day of the line; see
        /// <c>NotificationDetailViewModel.LoadTripSchedulesAsync</c>.
        /// </remarks>
        public async Task<List<ScheduleDto>> GetSchedulesByTripAsync(int tripId)
        {
            var response = await _httpClient.GetAsync($"{_endPoint}/by-trip/{tripId}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<ScheduleDto>>();
        }

        public async Task<List<UnscheduledTripDto>> GetUnscheduledTripsAsync(DateTime date)
        {
            var url = $"{_endPoint}/unscheduled?date={date:yyyy-MM-dd}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<UnscheduledTripDto>>();
        }      

        public async Task RouteTripsAsync(RouteTripRequest request)
        {
            //var request = new RouteTripRequest { VehicleRouteId = vehicleRouteId, TripIds = tripIds };
            var response = await _httpClient.PostAsJsonAsync($"{_endPoint}/route", request);

            // The API already says why it refused: the controller answers with
            // "Error: ... -- Detalle: ...", and a request that fails model binding gets the
            // ValidationProblemDetails that [ApiController] returns before the action runs.
            // EnsureSuccessStatusCode() threw that body away and left the dispatcher with a
            // bare "400 (Bad Request)" — nothing to act on, and nothing to report.
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new ApiException(
                    message: $"The server refused to route the trip ({(int)response.StatusCode}). {body}",
                    statusCode: response.StatusCode,
                    details: body);
            }

            // --- HISTORY RECORD ---
            await _historyService.SaveHistoryAsync(request.TripId, "Run", "Unassigned", request.VehicleRouteName);
        }

        public async Task CancelRouteAsync(int scheduleId)
        {
            var scheduleInfo = await _httpClient.GetFromJsonAsync<ScheduleDto>($"{_endPoint}/{scheduleId}");

            if (scheduleInfo != null && scheduleInfo.TripId.HasValue)
            {             
                var request = new CancelRouteRequest { ScheduleId = scheduleId };
                var response = await _httpClient.PostAsJsonAsync($"{_endPoint}/cancel-route", request);
                response.EnsureSuccessStatusCode();

                await _historyService.SaveHistoryAsync(scheduleInfo.TripId.Value, "Run", scheduleInfo.Run, "Unassigned (Route Cancelled)");
               
            }

            /*var request = new CancelRouteRequest { ScheduleId = scheduleId };
            var response = await _httpClient.PostAsJsonAsync($"{_endPoint}/cancel-route", request);
            response.EnsureSuccessStatusCode();

            await _historyService.SaveHistoryAsync(tripId, "Run", "Assigned", "Unassigned (Route Cancelled)");*/
        }

        public async Task UpdateAsync(int id, ScheduleDto dto)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"{_endPoint}/{id}", dto);

                // We do not expect content, just a success code (204 No Content)
                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error updating schedule");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Connection error with the server.", ex);
            }
        }

        public async Task<List<ProductionReportRowDto>> GetTrip2ReportDataAsync(DateTime startDate, DateTime endDate, List<int> fundingSourceIds)
        {
            string start = startDate.ToString("yyyy-MM-dd");
            string end = endDate.ToString("yyyy-MM-dd");

            // Reusing the logic for range and multi-ID filtering
            var requestUri = $"{_endPoint}/reports/production-range?startDate={start}&endDate={end}";

            if (fundingSourceIds != null && fundingSourceIds.Any())
            {
                string idsString = string.Join(",", fundingSourceIds);
                requestUri += $"&fundingSourceIds={idsString}";
            }

            var response = await _httpClient.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<ProductionReportRowDto>>();
        }

        public async Task<List<ProductionReportRowDto>> GetProductionReportDataRangeAsync(
            DateTime startDate,
            DateTime endDate,
            List<int> fundingSourceIds,
            List<int> vehicleRouteIds) 
        {
            string start = startDate.ToString("yyyy-MM-dd");
            string end = endDate.ToString("yyyy-MM-dd");

            var requestUri = $"{_endPoint}/reports/production-range?startDate={start}&endDate={end}";

            if (fundingSourceIds != null && fundingSourceIds.Any())
            {
                requestUri += $"&fundingSourceIds={string.Join(",", fundingSourceIds)}";
            }
          
            if (vehicleRouteIds != null && vehicleRouteIds.Any())
            {
                requestUri += $"&vehicleRouteIds={string.Join(",", vehicleRouteIds)}";
            }

            var response = await _httpClient.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<ProductionReportRowDto>>();
        }

        public async Task<List<ProductionReportRowDto>> GetProductionReportDataRangeAsync2(DateTime startDate, DateTime endDate, List<int> fundingSourceIds)
        {
            string start = startDate.ToString("yyyy-MM-dd");
            string end = endDate.ToString("yyyy-MM-dd");

            // Endpoint: reports/production-range
            var requestUri = $"{_endPoint}/reports/production-range?startDate={start}&endDate={end}";

            if (fundingSourceIds != null && fundingSourceIds.Any())
            {
                string idsString = string.Join(",", fundingSourceIds);
                requestUri += $"&fundingSourceIds={idsString}";
            }

            var response = await _httpClient.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<ProductionReportRowDto>>();
        }
        public async Task<List<ProductionReportRowDto>> GetProductionReportDataAsync(DateTime date, int? fundingSourceId)
        {
            // Format the date correctly for the URL query string.
            string formattedDate = date.ToString("yyyy-MM-dd");
            var requestUri = $"{_endPoint}/reports/production?date={formattedDate}";

            // --- NEW: Append the fundingSourceId if it has a value ---
            if (fundingSourceId.HasValue)
            {
                requestUri += $"&fundingSourceId={fundingSourceId.Value}";
            }

            var response = await _httpClient.GetAsync(requestUri);

            response.EnsureSuccessStatusCode(); // Throws an exception if the HTTP response is not successful.

            // The ReadFromJsonAsync method handles JSON deserialization for you.
            return await response.Content.ReadFromJsonAsync<List<ProductionReportRowDto>>();
        }

        public async Task<List<ProductionReportRowDto>> GetAviataReportDataAsync(DateTime startDate, DateTime endDate)
        {
            // Formateamos las fechas para el QueryString
            string start = startDate.ToString("yyyy-MM-dd");
            string end = endDate.ToString("yyyy-MM-dd");

            var requestUri = $"{_endPoint}/reports/aviata?startDate={start}&endDate={end}";

            var response = await _httpClient.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<ProductionReportRowDto>>();
        }

        public async Task<List<ProductionReportRowDto>> GetAviataReportDataAsync(DateTime startDate, DateTime endDate, List<int> fundingSourceIds)
        {
            // Format dates for the QueryString
            string start = startDate.ToString("yyyy-MM-dd");
            string end = endDate.ToString("yyyy-MM-dd");

            // Start building the URL
            var requestUri = $"{_endPoint}/reports/aviata?startDate={start}&endDate={end}";

            // If there are selected IDs, join them with commas and append to URL
            if (fundingSourceIds != null && fundingSourceIds.Any())
            {
                string idsString = string.Join(",", fundingSourceIds);
                requestUri += $"&fundingSourceIds={idsString}";
            }

            var response = await _httpClient.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<ProductionReportRowDto>>();
        }

        public async Task<List<ProductionReportRowDto>> GetAviataReportDataAsyncOld(DateTime startDate, DateTime endDate, List<int> fundingSourceIds)
        {
            string start = startDate.ToString("yyyy-MM-dd");
            string end = endDate.ToString("yyyy-MM-dd");

            // Join IDs as a comma-separated string: "1,5,10"
            string ids = string.Join(",", fundingSourceIds);

            var requestUri = $"{_endPoint}/reports/aviata?startDate={start}&endDate={end}&fundingSourceIds={ids}";

            var response = await _httpClient.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<ProductionReportRowDto>>();
        }

        private async Task<ApiException> CreateApiException(HttpResponseMessage response, string context)
        {
            try
            {
                var problemDetails = await response.Content.ReadFromJsonAsync<DTOs.ProblemDetails>();

                // Construct a clearer error message, using the issue title if available
                var errorMessage = $"{context}: {problemDetails?.Title ?? "Error not specified by the API."}";

                return new ApiException(
                    message: errorMessage,
                    statusCode: response.StatusCode,
                    details: problemDetails?.Detail);
            }
            catch // If the response body is not valid ProblemDetails JSON
            {
                var content = await response.Content.ReadAsStringAsync();
                return new ApiException(
                    message: $"{context}. Unexpected server response.",
                    statusCode: response.StatusCode,
                    details: content);
            }
        }

    }
}
